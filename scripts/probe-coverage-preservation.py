#!/usr/bin/env python3
"""Retain original Microsoft coverage format/merge probes; never establishes adoption.

Requires an already-installed dotnet-coverage 18.12.0. No packages are installed,
no product inputs are modified, and an existing evidence directory is refused.
"""
import argparse
import hashlib
import json
import re
import shutil
import subprocess
import time
from pathlib import Path
from xml.etree import ElementTree as ET


def sha(path):
    return hashlib.file_digest(path.open("rb"), "sha256").hexdigest()


def inventory(directory):
    return {str(path.relative_to(directory)): sha(path)
            for path in sorted(directory.rglob("*")) if path.is_file()}


def report(path):
    if not path.is_file():
        return {"exists": False}
    root = ET.parse(path).getroot()
    return {"exists": True, "sha256": sha(path), "attributes": root.attrib,
            "model": [{"class": cls.get("name"), "file": cls.get("filename"),
                       "methods": [{"name": method.get("name"), "signature": method.get("signature"),
                                    "lines": [line.get("number") for line in method.findall("./lines/line")]}
                                   for method in cls.findall("./methods/method")],
                       "lines": [{"number": line.get("number"), "branch": line.get("branch"),
                                  "branchDenominator": line.get("condition-coverage", "").split("/")[-1].rstrip(")") if line.get("condition-coverage") else None,
                                  "conditions": [{"number": condition.get("number"), "type": condition.get("type")}
                                                 for condition in line.findall("./conditions/condition")]}
                                 for line in cls.findall("./lines/line")]}
                      for cls in root.findall("./packages/package/classes/class")],
            "conditions": [{"class": cls.get("name"), "file": cls.get("filename"),
                            "line": line.attrib,
                            "conditions": [condition.attrib for condition in line.findall("./conditions/condition")]}
                           for cls in root.findall("./packages/package/classes/class")
                           for line in cls.findall("./lines/line") if line.get("branch", "").lower() == "true"],
            "note": "Aggregate switch percentages do not identify individual switch edges."}


def assess_live_union(reports, commands, inputs_unchanged):
    selected = [reports.get(name, {}) for name in
                ("a.xml", "b.xml", "union.xml", "shared-overlap.xml", "shared-same.xml")]
    models_equal = all(item.get("model") == selected[0].get("model") for item in selected)
    counts = [(item.get("attributes", {}).get("branches-valid"), item.get("attributes", {}).get("branches-covered"))
              for item in selected]
    return {"modelsEqual": models_equal, "observedCounts": counts,
            "demonstrated": models_equal and counts == [("5", "2"), ("5", "2"), ("5", "3"), ("5", "3"), ("5", "2")]
                            and bool(commands) and all(item["exitCode"] == 0 for item in commands) and inputs_unchanged,
            "scope": "Controlled five-edge switch only. Does not establish full product exclusions/model equality or hosted shard isolation."}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--tool", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--instrumentation", choices=("configured", "static", "dynamic"), default="configured")
    parser.add_argument("--unverifiable", action="store_true", help="Use documented UseVerifiableInstrumentation=False")
    parser.add_argument("--mtp", action="store_true", help="Also collect pinned repository MTP/xUnit fixture controls")
    args = parser.parse_args()
    tool = args.tool.resolve()
    output = args.output.resolve()
    output.mkdir(parents=True, exist_ok=False)
    shutil.copy2(Path(__file__).resolve(), output / "runner-original.py")
    evidence = {"schema": 1, "adoptionEligible": False, "commands": [],
                "sourceSupport": [
                    "https://learn.microsoft.com/en-us/dotnet/core/additional-tools/dotnet-coverage",
                    "https://github.com/microsoft/codecoverage/blob/main/docs/configuration.md",
                    "https://github.com/microsoft/codecoverage/blob/main/docs/instrumentation.md",
                    "https://github.com/microsoft/codecoverage/issues/11#issuecomment-1457935172"],
                "coverageContract": "Focused fixture only; no product/full-suite denominator claim."}

    def persist():
        (output / "evidence.json").write_text(json.dumps(evidence, indent=2) + "\n")

    def run(label, command):
        started = time.time()
        with (output / (label + ".log")).open("w") as log:
            result = subprocess.run([str(arg) for arg in command], stdout=log, stderr=subprocess.STDOUT)
        evidence["commands"].append({"label": label, "argv": [str(arg) for arg in command],
                                     "exitCode": result.returncode, "startedUnix": started,
                                     "elapsedSeconds": time.time() - started,
                                     "log": label + ".log"})
        persist()
        return result.returncode

    run("tool-version", [tool, "--version"])
    version = (output / "tool-version.log").read_text().strip()
    if not version.startswith("18.12.0+"):
        raise SystemExit("Expected pinned Microsoft dotnet-coverage 18.12.0; original version retained")
    evidence["toolVersion"] = version
    tool_store = tool.parent / ".store/dotnet-coverage/18.12.0"
    if not tool_store.is_dir():
        raise SystemExit("Expected local tool store for immutable tool input inventory")
    evidence["toolInputs"] = inventory(tool_store)
    run("sdk-version", ["dotnet", "--version"])
    run("sdk-list", ["dotnet", "--list-sdks"])
    run("sdk-info", ["dotnet", "--info"])
    selected = (output / "sdk-version.log").read_text().strip()
    sdk_paths = [Path(match.group(2)) / selected for match in
                 re.finditer(r"^(\S+) \[(.+)\]$", (output / "sdk-list.log").read_text(), re.M)
                 if match.group(1) == selected]
    if len(sdk_paths) != 1:
        raise SystemExit("Cannot establish selected SDK inventory")
    evidence["sdkInputs"] = inventory(sdk_paths[0])
    host = Path(shutil.which("dotnet")).resolve()
    evidence["dotnetHost"] = {"path": str(host), "sha256": sha(host)}
    fixture = Path(__file__).resolve().parent / "branch-preservation-probe"
    repository = Path(__file__).resolve().parent.parent
    evidence["repositoryBuildInputs"] = {name: sha(repository / name)
                                          for name in ("Directory.Build.props", "Directory.Build.targets", "Directory.Packages.props", "global.json", "NuGet.Config", "nuget.config")
                                          if (repository / name).is_file()}
    source = output / "source"
    source.mkdir()
    for name in ("Probe.csproj", "Program.cs", "settings.xml"):
        shutil.copy2(fixture / name, source / name)
    if args.mtp:
        for name in ("ProbeTests.csproj", "TestCases.cs"):
            shutil.copy2(fixture / name, source / name)
    build = output / "build"
    settings = ET.parse(source / "settings.xml")
    directories = ET.SubElement(settings.find("./CodeCoverage/ModulePaths"), "IncludeDirectories")
    ET.SubElement(directories, "Directory", Recursive="true").text = str(build)
    ET.SubElement(settings.find("./CodeCoverage"), "EnableStaticManagedInstrumentation").text = "True"
    if args.instrumentation != "configured":
        settings.find("./CodeCoverage/EnableStaticManagedInstrumentation").text = str(args.instrumentation == "static")
        ET.SubElement(settings.find("./CodeCoverage"), "EnableDynamicManagedInstrumentation").text = str(args.instrumentation == "dynamic")
    if args.unverifiable:
        ET.SubElement(settings.find("./CodeCoverage"), "UseVerifiableInstrumentation").text = "False"
    settings.write(source / "settings.xml", encoding="utf-8", xml_declaration=True)
    evidence["sourceInputs"] = inventory(source)
    if run("build", ["dotnet", "build", source / "Probe.csproj", "--configuration", "Release", "--output", build]):
        raise SystemExit("Original build failed; evidence retained")
    if args.mtp and run("mtp-build", ["dotnet", "build", source / "ProbeTests.csproj", "--configuration", "Release", "--output", build]):
        raise SystemExit("Original MTP build failed; evidence retained")
    evidence["buildInputs"] = inventory(build)
    assembly = build / "CoveragePreservationProbe.dll"
    run("switch-il", ["dotnet", assembly, "--il"])
    instrument = ["--include-files", str(assembly)] if args.instrumentation == "static" else []
    cases = {"a": ["0", "1"], "b": ["1", "2"], "union": ["0", "1", "2"]}
    commands = {label: ["dotnet", str(assembly), *values] for label, values in cases.items()}
    if args.mtp:
        test_assembly = build / "CoveragePreservationProbe.Tests.dll"
        commands = {label: ["dotnet", str(test_assembly), "--filter-method", f"CoveragePreservationFixture.Cases.{label.capitalize()}"]
                    for label in cases}
        for label, command in commands.items():
            run(f"{label}-mtp-control", [*command, "--coverage", "--coverage-output", str(output / f"{label}-mtp.xml"),
                                        "--coverage-output-format", "cobertura", "--coverage-settings", source / "settings.xml",
                                        "--report-xunit-trx", "--report-xunit-trx-filename", "original.trx",
                                        "--results-directory", output / f"{label}-mtp-results"])
        commands = {label: [*command, "--report-xunit-trx", "--report-xunit-trx-filename", "original.trx",
                            "--results-directory", str(output / f"{label}-cli-results")]
                    for label, command in commands.items()}
    for label, values in cases.items():
        for fmt, suffix in (("coverage", "coverage"), ("cobertura", "xml")):
            command = list(commands[label])
            if args.mtp:
                command[-1] = str(output / f"{label}-{fmt}-results")
            run(f"{label}-{fmt}", [tool, "collect", *instrument, "-s", source / "settings.xml", "-f", fmt,
                                   "-o", output / f"{label}.{suffix}", *command])
        run(f"{label}-export", [tool, "merge", output / f"{label}.coverage", "-f", "cobertura",
                                "-o", output / f"{label}-export.xml"])
    run("native-merge", [tool, "merge", output / "a.coverage", output / "b.coverage", "-f", "coverage",
                         "-o", output / "merged.coverage"])
    run("native-merge-export", [tool, "merge", output / "merged.coverage", "-f", "cobertura",
                                "-o", output / "merged-export.xml"])
    run("cobertura-merge", [tool, "merge", output / "a.xml", output / "b.xml", "-f", "cobertura",
                            "-o", output / "merged-cobertura.xml"])
    wrapper = output / "shared-session.py"
    wrapper.write_text('import json, subprocess, sys\n'
                       'from pathlib import Path\n'
                       'commands = json.load(open(sys.argv[1]))\n'
                       'root = Path(sys.argv[1]).with_suffix("")\n'
                       'root.mkdir()\n'
                       'logs = [(root / f"producer-{n}.log").open("w") for n in (1, 2)]\n'
                       'a = subprocess.Popen(commands[0], stdout=logs[0], stderr=subprocess.STDOUT)\n'
                       'b = subprocess.Popen(commands[1], stdout=logs[1], stderr=subprocess.STDOUT)\n'
                       'codes = [a.wait(), b.wait()]\n'
                       'for log in logs: log.close()\n'
                       '(root / "exit-codes.json").write_text(json.dumps(codes) + "\\n")\n'
                       'raise SystemExit(0 if codes == [0, 0] else 1)\n')
    evidence["sourceSupport"].append("https://github.com/microsoft/codecoverage/issues/104#issuecomment-1991051175")
    for label, second in (("shared-overlap", "1,2"), ("shared-same", "0,1")):
        command_file = output / f"{label}-commands.json"
        producer_commands = [list(commands["a"]), list(commands["b" if second == "1,2" else "a"])]
        if args.mtp:
            for number, command in enumerate(producer_commands, 1):
                command[-1] = str(output / f"{label}-producer-{number}-results")
        command_file.write_text(json.dumps(producer_commands, indent=2) + "\n")
        run(label, [tool, "collect", *instrument, "-s", source / "settings.xml", "-f", "cobertura",
                    "-o", output / f"{label}.xml", "python3", wrapper, command_file])
    evidence["reports"] = {path.name: report(path) for path in sorted(output.glob("*.xml"))}
    evidence["buildUnchanged"] = inventory(build) == evidence["buildInputs"]
    evidence["sdkUnchanged"] = inventory(sdk_paths[0]) == evidence["sdkInputs"]
    evidence["toolUnchanged"] = inventory(tool_store) == evidence["toolInputs"]
    evidence["sourceUnchanged"] = all((source / name).is_file() and sha(source / name) == digest
                                      for name, digest in evidence["sourceInputs"].items())
    evidence["dotnetHostUnchanged"] = sha(host) == evidence["dotnetHost"]["sha256"]
    evidence["repositoryBuildInputsUnchanged"] = all((repository / name).is_file() and sha(repository / name) == digest
                                                     for name, digest in evidence["repositoryBuildInputs"].items())
    evidence["preservationAssessment"] = {
        "positiveBranchDenominators": all(int(item["attributes"].get("branches-valid", "0")) > 0
                                           for item in evidence["reports"].values()),
        "identityUnionProven": False,
        "reason": "Probe reports alone never authorize adoption. Missing branch denominators reject preservation; aggregate counters cannot prove individual switch-edge identities."
    }
    evidence["controlledLiveUnion"] = assess_live_union(evidence["reports"], evidence["commands"],
                                                       all(evidence[key] for key in ("sourceUnchanged", "buildUnchanged", "sdkUnchanged", "toolUnchanged", "dotnetHostUnchanged", "repositoryBuildInputsUnchanged")))
    evidence["originalArtifacts"] = {str(path.relative_to(output)): sha(path)
                                     for path in sorted(output.rglob("*"))
                                     if path.is_file() and path.name != "evidence.json"}
    persist()
    print(output / "evidence.json")


if __name__ == "__main__":
    main()
