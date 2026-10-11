#!/usr/bin/env python3
"""Summarize retained cost probes, with non-additive wall/exclusive span attribution."""

from collections import defaultdict
from datetime import datetime
import importlib.util
import json
from pathlib import Path
import sys
import xml.etree.ElementTree as ET

SPEC = importlib.util.spec_from_file_location("ci_duration_report", Path(__file__).with_name("ci-duration-report.py"))
TIMING = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(TIMING)


def union(intervals):
    total, end = 0.0, None
    for start, stop in sorted(intervals):
        if stop < start:
            raise ValueError("Negative monotonic span")
        if end is None or start > end:
            total += stop - start
        elif stop > end:
            total += stop - end
        end = max(end, stop) if end is not None else stop
    return total


def summarize(directory):
    result = json.loads((directory / "result.json").read_text())
    trx = TIMING.summarize(directory / "backend.trx")
    events = [json.loads(line) for path in (directory / "phases").glob("*.jsonl") for line in path.read_text().splitlines()]
    spans = {(row["processId"], row["spanId"]): row for row in events if row.get("eventType") == "span"}
    if len(spans) != sum(row.get("eventType") == "span" for row in events):
        raise ValueError("Duplicate process/span identity")
    children = defaultdict(list)
    for key, span in spans.items():
        if span["parentSpanId"] is not None:
            parent_key = (span["processId"], span["parentSpanId"])
            if parent_key not in spans:
                raise ValueError("Missing parent span")
            parent = spans[parent_key]
            if span["startTicks"] < parent["startTicks"] or span["endTicks"] > parent["endTicks"]:
                raise ValueError("Child span outside its parent")
            children[parent_key].append(span)
    phases = defaultdict(lambda: {"count": 0, "inclusiveSeconds": 0.0, "exclusiveSeconds": 0.0, "outcomes": defaultdict(int)})
    cases = defaultdict(lambda: {"methodName": None, "rootIntervals": []})
    for key, span in spans.items():
        frequency = span["frequency"]
        inclusive = (span["endTicks"] - span["startTicks"]) / frequency
        exclusive = inclusive - union([(row["startTicks"] / frequency, row["endTicks"] / frequency) for row in children[key]])
        if exclusive < -1e-6:
            raise ValueError("Invalid exclusive interval coverage")
        phase = phases[span["phase"]]
        phase["count"] += 1
        phase["inclusiveSeconds"] += inclusive
        phase["exclusiveSeconds"] += max(0, exclusive)
        phase["outcomes"][span["outcome"]] += 1
        if span["caseId"] and span["parentSpanId"] is None:
            case = cases[(span["processId"], span["caseId"])]
            case["methodName"] = span.get("methodName")
            case["rootIntervals"].append((span["startTicks"] / frequency, span["endTicks"] / frequency))
    ns = TIMING.NS
    root = ET.parse(directory / "backend.trx").getroot()
    definitions = {row.get("id"): row.find("t:TestMethod", ns) for row in root.findall("t:TestDefinitions/t:UnitTest", ns)}
    by_method = defaultdict(lambda: {"caseCount": 0, "trxSeconds": 0.0, "probeRootSeconds": 0.0, "probedExecutions": 0})
    for row in root.findall("t:Results/t:UnitTestResult", ns):
        definition = definitions[row.get("testId")]
        name = definition.get("className") + "." + definition.get("name")
        by_method[name]["caseCount"] += 1
        by_method[name]["trxSeconds"] += TIMING.seconds(row.get("duration"))
    for case in cases.values():
        if case["methodName"]:
            metric = by_method[case["methodName"]]
            metric["probeRootSeconds"] += union(case["rootIntervals"])
            metric["probedExecutions"] += 1
    resources = [json.loads(line) for line in (directory / "resources.jsonl").read_text().splitlines()]
    process_max = defaultdict(lambda: {"cpuSeconds": 0.0, "readBytes": 0, "writeBytes": 0})
    container_max = defaultdict(lambda: {"readBytes": 0, "writeBytes": 0})
    for row in resources:
        for container in row["containers"]:
            for name in ("readBytes", "writeBytes"):
                container_max[container["containerId"]][name] = max(container_max[container["containerId"]][name], container[name] or 0)
        for process in row["processes"]:
            metric = process_max[(process["pid"], process["startTimeTicks"])]
            metric["cpuSeconds"] = max(metric["cpuSeconds"], process["userCpuSeconds"] + process["systemCpuSeconds"])
            for name in ("readBytes", "writeBytes"):
                metric[name] = max(metric[name], process[name] or 0)
    return {
        "run": directory.name,
        "result": result,
        "trxSeconds": trx["run_seconds"],
        "postgresSpanSeconds": trx["postgres_span_seconds"],
        "containerSampling": {
            "sampleCount": sum(len(row["containers"]) for row in resources),
            "peakCpuPercent": max((c["cpuPercent"] for row in resources for c in row["containers"]), default=None),
            "peakRssBytes": max((c["rssBytes"] or 0 for row in resources for c in row["containers"]), default=None),
            "observedReadBytesLowerBound": sum(value["readBytes"] for value in container_max.values()),
            "observedWriteBytesLowerBound": sum(value["writeBytes"] for value in container_max.values()),
        },
        "phases": dict(sorted(phases.items())),
        "methods": {name: value | {"rootAttributionRatio": value["probeRootSeconds"] / value["trxSeconds"] if value["trxSeconds"] else None} for name, value in sorted(by_method.items()) if value["probedExecutions"]},
        "sampling": {"count": len(resources), "errorCount": sum(row.get("error") is not None for row in resources), "samplingSeconds": sum(row["samplingSeconds"] for row in resources), "peakProcessRssSumBytes": max((sum(p["rssBytes"] for p in row["processes"]) for row in resources), default=0), "observedProcessCpuSecondsLowerBound": sum(value["cpuSeconds"] for value in process_max.values()), "observedProcessReadBytesLowerBound": sum(value["readBytes"] for value in process_max.values()), "observedProcessWriteBytesLowerBound": sum(value["writeBytes"] for value in process_max.values()), "peakTcpStatesHex": {state: max(row["tcpStatesHex"].get(state, 0) for row in resources) for state in sorted({state for row in resources for state in row["tcpStatesHex"]})}},
    }


if __name__ == "__main__":
    if len(sys.argv) != 2:
        sys.exit("Usage: python3 -B scripts/analyze-backend-cost.py <measurement-root>")
    root = Path(sys.argv[1])
    print(json.dumps([summarize(path.parent) for path in sorted(root.glob("*-*/result.json"))], indent=2))
