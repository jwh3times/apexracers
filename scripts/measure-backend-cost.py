#!/usr/bin/env python3
"""Run original, alternating Release cost measurements; this is not an acceptance gate.

Requires an already built Release tree, Linux /proc, Docker, and a fresh output directory.
Stops at the first original failure or inventory/build change. It never retries a case.
"""

import argparse
from collections import Counter
from datetime import datetime, timezone
import hashlib
import json
import os
from pathlib import Path
import platform
import re
import shutil
import subprocess
import threading
import time
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[1]
TEST_OUTPUT = ROOT / "src/ApexRacers.Tests/bin/Release/net10.0"
NS = {"t": "http://microsoft.com/schemas/VisualStudio/TeamTest/2010"}


def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def inventory():
    tracked = subprocess.check_output(["git", "ls-files", "-z", "--", "src"], cwd=ROOT).decode().split("\0")
    paths = [ROOT / name for name in tracked if name]
    paths += [ROOT / "docs/testing/driver-catalog-admission-v1.json"]
    paths += [ROOT / name for name in ("global.json", "coverage.runsettings", "Directory.Packages.props", "Directory.Build.props", "Directory.Build.targets") if (ROOT / name).is_file()]
    paths += [path for path in TEST_OUTPUT.rglob("*") if path.is_file()]
    return {str(path.relative_to(ROOT)): digest(path) for path in sorted(paths)}


def sdk_inventory():
    version = subprocess.check_output(["dotnet", "--version"], text=True).strip()
    installs = subprocess.check_output(["dotnet", "--list-sdks"], text=True).splitlines()
    match = next(re.fullmatch(r"([^ ]+) \[(.+)\]", row) for row in installs if row.startswith(version + " "))
    directory = Path(match[2]) / version
    files = {str(path.relative_to(directory)): digest(path) for path in sorted(directory.rglob("*")) if path.is_file()}
    return {"version": version, "hostSha256": digest(Path(shutil.which("dotnet")).resolve()), "files": files}


def read_results(path):
    root = ET.parse(path).getroot()
    results = root.findall("t:Results/t:UnitTestResult", NS)
    return Counter(item.get("testName") for item in results), Counter(item.get("outcome") for item in results)


def numeric_process(pid):
    """Only numerical /proc counters; never read argv, environments, or connection strings."""
    try:
        base = Path(f"/proc/{pid}")
        stat = (base / "stat").read_text().rsplit(") ", 1)[1].split()
        io = {}
        try:
            io = {line.split(":")[0]: int(line.split(":")[1]) for line in (base / "io").read_text().splitlines()}
        except (OSError, ValueError):
            pass
        return {"pid": pid, "parentPid": int(stat[1]), "startTimeTicks": int(stat[19]), "userCpuSeconds": int(stat[11]) / os.sysconf("SC_CLK_TCK"), "systemCpuSeconds": int(stat[12]) / os.sysconf("SC_CLK_TCK"), "rssBytes": int(stat[21]) * os.sysconf("SC_PAGE_SIZE"), "readBytes": io.get("read_bytes"), "writeBytes": io.get("write_bytes")}
    except (OSError, ValueError, IndexError):
        return None


def owned_processes(root_pid, recorded):
    rows = {}
    for entry in Path("/proc").iterdir():
        if entry.name.isdigit():
            row = numeric_process(int(entry.name))
            if row:
                rows[row["pid"]] = row
    return select_owned_processes(rows, root_pid, recorded)


def select_owned_processes(rows, root_pid, recorded):
    owned = {pid for pid, identity in recorded.items() if pid in rows and rows[pid]["startTimeTicks"] == identity}
    if root_pid not in owned:
        return []
    while True:
        added = {pid for pid, row in rows.items() if row["parentPid"] in owned} - owned
        if not added:
            break
        owned.update(added)
    return [rows[pid] for pid in sorted(owned) if pid in rows]


def memory_bytes(value):
    match = re.fullmatch(r"([0-9.]+)\s*([A-Za-z]+)", value.strip())
    if not match:
        return None
    units = {"B": 1, "kB": 1000, "MB": 10**6, "GB": 10**9, "TB": 10**12, "KiB": 1024, "MiB": 1024**2, "GiB": 1024**3, "TiB": 1024**4}
    scale = units.get(match[2])
    return float(match[1]) * scale if scale else None


def container_samples(ids):
    if not ids:
        return []
    result = subprocess.run(["docker", "stats", "--no-stream", "--format", "{{json .}}", *sorted(ids)], capture_output=True, text=True, timeout=8)
    if result.returncode:
        raise RuntimeError("container-sample-unavailable")
    rows = []
    for line in result.stdout.splitlines():
        data = json.loads(line)
        memory = data.get("MemUsage", "").split(" / ")[0]
        disk = data.get("BlockIO", "").split(" / ")
        rows.append({"containerId": data["ID"], "cpuPercent": float(data["CPUPerc"].rstrip("%")), "rssBytes": memory_bytes(memory), "readBytes": memory_bytes(disk[0]) if disk else None, "writeBytes": memory_bytes(disk[1]) if len(disk) == 2 else None})
    return rows


def tcp_states(ports):
    """Host TCP sockets touching owned mapped ports; counts are not DB backend counts."""
    counts = Counter()
    for name in ("tcp", "tcp6"):
        try:
            for line in Path(f"/proc/net/{name}").read_text().splitlines()[1:]:
                fields = line.split()
                local = int(fields[1].split(":")[1], 16)
                remote = int(fields[2].split(":")[1], 16)
                if local in ports or remote in ports:
                    counts[fields[3]] += 1
        except (OSError, ValueError, IndexError):
            pass
    return dict(counts)


def monitor(root_pid, probe_dir, output, stop):
    positions = {}
    root = numeric_process(root_pid)
    processes = {root_pid: root["startTimeTicks"]} if root else {}
    containers, ports = set(), set()
    with output.open("w") as stream:
        while not stop.is_set():
            for path in probe_dir.glob("*.jsonl"):
                with path.open() as source:
                    source.seek(positions.get(path, 0))
                    while True:
                        start = source.tell()
                        line = source.readline()
                        if not line or not line.endswith("\n"):
                            source.seek(start)
                            break
                        event = json.loads(line)
                        if event.get("eventType") == "resource":
                            if event.get("kind") == "process":
                                pid = int(event["id"])
                                process = numeric_process(pid)
                                if process and process["startTimeTicks"] / os.sysconf("SC_CLK_TCK") <= event["ticks"] / event["frequency"]:
                                    processes[pid] = process["startTimeTicks"]
                            if event.get("kind") == "container" and re.fullmatch(r"[0-9a-fA-F]{64}", event["id"]):
                                if event.get("lifecycle") == "disposed":
                                    containers.discard(event["id"])
                                else:
                                    containers.add(event["id"])
                                if event.get("port", 0) > 0:
                                    ports.add(int(event["port"]))
                    positions[path] = source.tell()
            start = time.monotonic()
            try:
                container_rows = container_samples(containers)
                error = None
            except (OSError, ValueError, RuntimeError, subprocess.TimeoutExpired):
                container_rows, error = [], "container-sample-unavailable"
            row = {"monotonicSeconds": start, "processes": owned_processes(root_pid, processes), "containers": container_rows, "tcpStatesHex": tcp_states(ports), "samplingSeconds": time.monotonic() - start, "error": error}
            stream.write(json.dumps(row) + "\n")
            stream.flush()
            stop.wait(2)


def monitored(root_pid, probe_dir, output, stop):
    try:
        monitor(root_pid, probe_dir, output, stop)
    except Exception as error:
        output.with_suffix(".error.json").write_text(json.dumps({"errorType": type(error).__name__}) + "\n")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("output", type=Path)
    parser.add_argument("--pairs", type=int, default=3)
    args = parser.parse_args()
    if args.pairs < 1:
        parser.error("pairs must be positive")
    output = args.output.resolve()
    output.mkdir(parents=True, exist_ok=False)
    binary = TEST_OUTPUT / "ApexRacers.Tests"
    if not binary.exists():
        parser.error("Build Release before measuring")
    expected_build = inventory()
    expected_sdk = sdk_inventory()
    (output / "sdk-inventory.json").write_text(json.dumps(expected_sdk, indent=2) + "\n")
    discovery = subprocess.run([str(binary), "--list-tests", "json"], cwd=ROOT, capture_output=True, text=True, check=True)
    discovered = json.loads(discovery.stdout)
    names = Counter(item["displayName"] for item in discovered["tests"])
    if not names:
        raise ValueError("Empty discovered inventory")
    (output / "discovery.json").write_text(json.dumps(discovered, indent=2) + "\n")
    context = {"schemaVersion": 1, "scope": "cost measurement only; not acceptance evidence", "startedAt": datetime.now(timezone.utc).isoformat(), "head": subprocess.check_output(["git", "rev-parse", "HEAD"], cwd=ROOT, text=True).strip(), "dirtyState": subprocess.check_output(["git", "status", "--porcelain"], cwd=ROOT, text=True).splitlines(), "platform": platform.platform(), "logicalCpus": os.cpu_count(), "sdk": subprocess.check_output(["dotnet", "--version"], text=True).strip(), "docker": subprocess.check_output(["docker", "version", "--format", "{{.Server.Version}}"], text=True).strip(), "inventory": expected_build, "discoverySha256": digest(output / "discovery.json"), "runnerSha256": digest(Path(__file__)), "count": sum(names.values())}
    context.update(sdkInventorySha256=digest(output / "sdk-inventory.json"), processClockTicksPerSecond=os.sysconf("SC_CLK_TCK"))
    (output / "context.json").write_text(json.dumps(context, indent=2) + "\n")
    shutil.copy2(Path(__file__), output / "runner-original.py")
    for index in range(args.pairs * 2):
        coverage = bool(index % 2)
        directory = output / f"{index + 1}-{'coverage' if coverage else 'plain'}"
        directory.mkdir()
        probe = directory / "phases"
        probe.mkdir()
        if inventory() != expected_build or sdk_inventory() != expected_sdk:
            raise ValueError("Source/build inventory changed before original run")
        env = os.environ.copy()
        env["APEXRACERS_COST_PROBE_DIR"] = str(probe)
        env["DRIVER_PRIVACY_EVIDENCE_DIR"] = str(directory / "artifacts")
        command = ["dotnet", "test", "--project", "src/ApexRacers.Tests/ApexRacers.Tests.csproj", "--configuration", "Release", "--no-build", "--report-xunit-trx", "--report-xunit-trx-filename", "backend.trx", "--results-directory", str(directory)]
        if coverage:
            command += ["--coverage", "--coverage-output", "coverage.cobertura.xml", "--coverage-output-format", "cobertura", "--coverage-settings", str(ROOT / "coverage.runsettings")]
        start = time.monotonic()
        with (directory / "console.log").open("w") as log:
            process = subprocess.Popen(command, cwd=ROOT, env=env, stdout=log, stderr=subprocess.STDOUT)
            stop = threading.Event()
            worker = threading.Thread(target=monitored, args=(process.pid, probe, directory / "resources.jsonl", stop), daemon=True)
            worker.start()
            try:
                exit_code = process.wait()
                command_finished = time.monotonic()
            finally:
                stop.set()
                worker.join(timeout=15)
        result = {"coverage": coverage, "exitCode": exit_code, "commandSeconds": command_finished - start, "samplerShutdownSeconds": time.monotonic() - command_finished, "sourceBuildUnchanged": inventory() == expected_build, "sdkUnchanged": sdk_inventory() == expected_sdk}
        result["monitorValid"] = not worker.is_alive() and not (directory / "resources.error.json").exists() and (directory / "resources.jsonl").is_file() and (directory / "resources.jsonl").stat().st_size > 0
        if (directory / "backend.trx").exists():
            actual, outcomes = read_results(directory / "backend.trx")
            result.update(inventoryMatches=actual == names, outcomes=dict(outcomes), count=sum(actual.values()))
        else:
            result.update(inventoryMatches=False, outcomes={}, count=0)
        result["probeInvalid"] = "FIXTURE_COST_PROBE_INVALID" in (directory / "console.log").read_text()
        if coverage:
            report = directory / "coverage.cobertura.xml"
            result["coverageValid"] = False
            if report.exists():
                root = ET.parse(report).getroot()
                result["coverageValid"] = bool(root.findall("packages/package")) and int(root.get("lines-valid", "0")) > 0 and int(root.get("branches-valid", "0")) > 0
                result["lineRate"] = float(root.get("line-rate", "0"))
                result["branchRate"] = float(root.get("branch-rate", "0"))
                result["coverageGatesPass"] = result["lineRate"] >= 0.85 and result["branchRate"] >= 0.85
        (directory / "result.json").write_text(json.dumps(result, indent=2) + "\n")
        print(json.dumps({"run": directory.name, **result}), flush=True)
        if exit_code or not result["sourceBuildUnchanged"] or not result["sdkUnchanged"] or not result["monitorValid"] or not result["inventoryMatches"] or result["probeInvalid"] or result["outcomes"] != {"Passed": sum(names.values())} or (coverage and (not result["coverageValid"] or not result.get("coverageGatesPass", False))):
            return 1
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
