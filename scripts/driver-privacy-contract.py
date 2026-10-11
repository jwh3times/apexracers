#!/usr/bin/env python3
"""Collect bounded synthetic Driver evidence. Reports, not scenario labels, own outcomes."""
import argparse
import datetime as dt
import hashlib
import json
import pathlib
import platform
import re
import subprocess
import sys
import uuid
import xml.etree.ElementTree as ET

ROOT = pathlib.Path(__file__).resolve().parent.parent
NS = {"t": "http://microsoft.com/schemas/VisualStudio/TeamTest/2010"}


def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def now():
    return dt.datetime.now(dt.timezone.utc).isoformat()


def git(*args):
    return subprocess.check_output(["git", *args], cwd=ROOT).decode().strip()


def source_fingerprint():
    paths = subprocess.check_output(["git", "ls-files", "-z", "--cached", "--others", "--exclude-standard"], cwd=ROOT).decode().split("\0")
    hashes = {p: digest(ROOT / p) for p in sorted(set(paths)) if p and (ROOT / p).is_file() and (p.startswith(("src/", "web/src/", "web/privacy-e2e/", "docs/testing/")) or p in {"web/playwright.privacy.config.ts", "web/package.json", "web/package-lock.json", "global.json", "Directory.Packages.props", "Directory.Build.props", "docs/design/driver-acceptance-and-rollout.md", ".github/workflows/driver-privacy-contract.yml", "scripts/driver-privacy-contract.py", "scripts/driver_privacy_contract_tests.py"})}
    return hashlib.sha256(json.dumps(hashes, sort_keys=True).encode()).hexdigest()


def build_inventories():
    names = ["Api", "Core", "Data", "Ingestion", "Seeder", "Tests"]
    directories = [ROOT / ("src/ApexRacers." + name + "/bin/Release/net10.0") for name in names]
    assemblies = {str(p.relative_to(ROOT)): digest(p) for directory in directories for p in directory.rglob("*.dll")}
    configuration = {str(p.relative_to(ROOT)): digest(p) for directory in directories for p in directory.glob("*")
                     if p.is_file() and (p.name.endswith((".runtimeconfig.json", ".deps.json")) or p.name in {directory.parent.parent.parent.name, directory.parent.parent.parent.name + ".exe"})}
    spa = {str(p.relative_to(ROOT / "web/dist")): digest(p) for p in (ROOT / "web/dist").rglob("*") if p.is_file()}
    required = {"src/ApexRacers." + name + "/bin/Release/net10.0/ApexRacers." + name + ".dll" for name in names}
    required |= {"src/ApexRacers.Tests/bin/Release/net10.0/ApexRacers." + name + ".dll" for name in names}
    runtime = {"src/ApexRacers." + name + "/bin/Release/net10.0/ApexRacers." + name + extension
               for name in ["Api", "Seeder", "Tests"] for extension in [".runtimeconfig.json", ".deps.json"]}
    if not required.issubset(assemblies) or not runtime.issubset(configuration) or "index.html" not in spa or not any(name.startswith("assets/") for name in spa):
        raise ValueError("Complete Release assembly and built-SPA inventories are required")
    return {"compiledReleaseAssemblies": assemblies, "compiledReleaseConfiguration": configuration, "builtSpa": spa}


def validate_builds(context):
    for name, current in build_inventories().items():
        if current != context.get(name):
            raise ValueError("Compiled artifact inventory changed during evidence run: " + name)


def instant(value):
    if not isinstance(value, str):
        raise ValueError("Invalid execution timestamp")
    result = dt.datetime.fromisoformat(value.replace("Z", "+00:00"))
    if result.utcoffset() is None:
        raise ValueError("Execution timestamp requires an explicit timezone")
    return result


def backend_results(path):
    root = ET.parse(path).getroot()
    definitions = {}
    for test in root.findall(".//t:UnitTest", NS):
        method = test.find("t:TestMethod", NS)
        if method is None or not method.get("className") or not method.get("name") or test.get("id") in definitions:
            raise ValueError("Missing or duplicate backend case definition")
        definitions[test.get("id")] = method.get("className") + "." + method.get("name")
    records = []
    for result in root.findall(".//t:UnitTestResult", NS):
        records.append({"case": definitions[result.get("testId")], "name": result.get("testName"),
                        "result": result.get("outcome"), "executionId": result.get("executionId")})
    counters = root.find(".//t:Counters", NS)
    if not records or counters is None or int(counters.get("total")) != len(records):
        raise ValueError("Incomplete backend result inventory")
    if len({r["executionId"] for r in records}) != len(records):
        raise ValueError("Duplicate backend execution records")
    times = root.find("t:Times", NS)
    if times is None:
        raise ValueError("Missing backend execution timestamps")
    return records, times.get("start"), times.get("finish")


def browser_results(path):
    document = json.loads(path.read_text())
    records = []
    def visit(suite):
        for spec in suite.get("specs", []):
            for test in spec.get("tests", []):
                runs = test.get("results", [])
                passed = test.get("status") == "expected" and len(runs) == 1 and runs[0].get("status") == "passed" and runs[0].get("retry") == 0
                records.append({"case": spec["title"], "project": test.get("projectName"),
                                "result": "Passed" if passed else "Failed", "attempts": len(runs)})
        for child in suite.get("suites", []):
            visit(child)
    for suite in document.get("suites", []):
        visit(suite)
    if not records or document.get("errors"):
        raise ValueError("Missing browser results or browser infrastructure errors")
    return records, document["stats"]["startTime"]


def row_result(row, backend, browser):
    evidence = []
    for layer, required, records in [("backend", row["backend"], backend), ("browser", row["browser"], browser)]:
        for name in required:
            matches = [r for r in records if r["case"] == name]
            evidence.append({"layer": layer, "case": name, "executions": matches,
                             "result": "not exercised" if len(matches) < row.get("minimumBackendCases", {}).get(name, 1) else "passed" if all(r["result"] == "Passed" for r in matches) else "failed"})
    result = "failed" if any(e["result"] == "failed" for e in evidence) else "not exercised" if not evidence or any(e["result"] == "not exercised" for e in evidence) else "passed"
    return {**row, "result": result, "evidence": evidence,
            "fullRequirementResult": "not exercised" if row["notExercised"] else result}


def validate_admissions(directory, started):
    paths = sorted((directory / "admitted-catalog").glob("*.json"))
    if {p.stem for p in paths} != {"visitor-signed", "owner", "owner2"}:
        raise ValueError("All three actually admitted useful contexts are required")
    receipt = json.loads((ROOT / "docs/testing/driver-catalog-admission-v1.json").read_text())
    for name, expected in receipt["reviewedSources"].items():
        if hashlib.sha256((ROOT / name).read_bytes().replace(b"\r\n", b"\n")).hexdigest() != expected:
            raise ValueError("Material reviewed template change requires renewed maintainer decision")
    epochs = set()
    for path in paths:
        case = json.loads(path.read_text())
        if case["Passed"] is not True or case["Review"] != receipt or instant(case["ExecutedAt"]) < started:
            raise ValueError("Stale, failed or unbound catalog admission")
        original = case["OriginalEvidence"]
        if not original["CopyId"] or not original["PurposeId"] or not case["Grants"] or len({h["ProcessId"] for h in case["Hosts"]}) != 2:
            raise ValueError("Missing actual copy/purpose/consent/two-process binding")
        history = case["History"]
        if not history["Available"] or not history["Epoch"] or history["Epoch"] in epochs:
            raise ValueError("Isolated genesis and continuous history required")
        epochs.add(history["Epoch"])
        releases = history["Releases"]
        if len(releases) != (2 if path.stem == "visitor-signed" else 1) or any(not r["Terminal"] or r["ProvenUnsent"] for r in releases):
            raise ValueError("Useful output did not retain actual terminal release accounting")
    return paths


def validate_matrix_source(mapping):
    actual = hashlib.sha256((ROOT / mapping["matrixSource"]).read_bytes().replace(b"\r\n", b"\n")).hexdigest()
    if actual != mapping.get("matrixSourceSha256"):
        raise ValueError("Accepted matrix requirements changed; deliberately review and remap affected evidence")


def collect(directory, backend_exit, browser_exit):
    if (directory / "manifest.json").exists():
        raise ValueError("Finalized evidence cannot be overwritten; retain the original outcome and use a fresh run")
    errors = []
    try:
        context = json.loads((directory / "context.json").read_text())
        started = instant(context["startedAt"])
        if not context["sourceFingerprint"] or not context["commit"]:
            raise ValueError("Incomplete execution context")
    except (OSError, ValueError, KeyError, TypeError, AttributeError) as error:
        errors.append("execution context: " + str(error))
        context = {"startedAt": now(), "sourceFingerprint": None, "commit": None}
        started = instant(context["startedAt"])
    try:
        validate_builds(context)
    except (OSError, ValueError, KeyError, TypeError) as error:
        errors.append("compiled artifacts: " + str(error))
    backend, browser, admissions = [], [], []
    for report, loader in [("backend.trx", backend_results), ("browser.json", browser_results)]:
        try:
            loaded = loader(directory / report)
            if instant(loaded[1]) < started:
                raise ValueError("Report predates this evidence run")
            if report == "backend.trx":
                backend = loaded[0]
            else:
                browser = loaded[0]
        except (OSError, ValueError, KeyError, TypeError, AttributeError, ET.ParseError) as error:
            errors.append(report + ": " + str(error))
    try:
        admissions = validate_admissions(directory, started)
    except (OSError, ValueError, KeyError, TypeError, AttributeError) as error:
        errors.append("catalog admission: " + str(error))
    if backend_exit != 0 or browser_exit != 0:
        errors.append("A test process failed; successful retries cannot erase that outcome")
    if any(r["result"] != "Passed" for r in backend + browser):
        errors.append("Failed, skipped or inconclusive execution in the original reports")
    if source_fingerprint() != context["sourceFingerprint"]:
        errors.append("Material source/configuration change during evidence run")
    mapping = json.loads((ROOT / "docs/testing/driver-privacy-matrix.json").read_text())
    try:
        validate_matrix_source(mapping)
    except (OSError, ValueError, KeyError, TypeError) as error:
        errors.append("matrix source: " + str(error))
    expected = re.findall(r"^\| ([A-Z]+-\d+) \|", (ROOT / mapping["matrixSource"]).read_text(), re.M)
    rows = [row_result(row, backend, browser) for row in mapping["rows"]]
    if [row["id"] for row in rows] != expected or len(set(expected)) != len(expected):
        errors.append("Matrix row inventory differs from the accepted matrix")
    if any(row["result"] != "passed" for row in rows):
        errors.append("A required bounded synthetic row is failed or not exercised")
    artifacts = [{"path": str(p.relative_to(directory)), "sha256": digest(p)} for p in sorted(directory.rglob("*")) if p.is_file() and p.name != "manifest.json"]
    manifest = {"schemaVersion": 1, "scope": "controlled synthetic implementation only", "context": context,
                "finishedAt": now(), "syntheticGate": "failed" if errors else "passed",
                "fullMatrix": "not exercised", "liveEligible": False, "errors": errors,
                "catalogAdmissionArtifacts": [str(p.relative_to(directory)) for p in admissions],
                "rows": rows, "artifacts": artifacts,
                "notExercised": ["Real/provider proof and processing permissions", "Independent production authority and deployed backup/log/restore expiry", "Live quota and operational rollout/pilot observations"]}
    (directory / "manifest.json").write_text(json.dumps(manifest, indent=2) + "\n")
    print(json.dumps({"syntheticGate": manifest["syntheticGate"], "rows": len(rows), "errors": errors}))
    return 1 if errors else 0


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("command", choices=["start", "collect"])
    parser.add_argument("directory", type=pathlib.Path)
    parser.add_argument("--backend-exit", type=int)
    parser.add_argument("--browser-exit", type=int)
    args = parser.parse_args()
    directory = args.directory.resolve()
    if args.command == "start":
        directory.mkdir(parents=True, exist_ok=True)
        if any(directory.iterdir()):
            parser.error("Use a fresh empty evidence directory; retained prior failure evidence cannot be overwritten")
        context = {"runId": str(uuid.uuid4()), "startedAt": now(), "commit": git("rev-parse", "HEAD"),
                   "dirty": bool(git("status", "--porcelain")), "sourceFingerprint": source_fingerprint(),
                   "environment": platform.platform(), "provenance": "Demo / fabricated identities and measurements",
                   "configuration": {"backend": "Release, real PostgreSQL18 migrations and two-process HTTP fixtures", "browser": "built SPA, Chromium, two controlled hosts plus ordinary Demo, retries=0", "authority": "test-only proof/journal/history/operating adapters; ordinary Real startup unavailable"},
                   "catalogReviewVersion": "catalog30-technical-review-v1",
                   **build_inventories(),
                   "workflowRun": __import__("os").environ.get("GITHUB_RUN_ID"), "workflowAttempt": __import__("os").environ.get("GITHUB_RUN_ATTEMPT")}
        (directory / "context.json").write_text(json.dumps(context, indent=2) + "\n")
        return 0
    if args.backend_exit is None or args.browser_exit is None:
        parser.error("Original backend and browser process exit outcomes are required")
    try:
        return collect(directory, args.backend_exit, args.browser_exit)
    except ValueError as error:
        print(str(error), file=sys.stderr)
        return 1


if __name__ == "__main__":
    sys.exit(main())
