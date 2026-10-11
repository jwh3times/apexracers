#!/usr/bin/env python3
"""Read retained MTP TRX timing evidence; never modify or merge test results.

Usage: python3 scripts/ci-duration-report.py report.trx [report.trx ...]
Stdout is a JSON summary. Durations are seconds, not additive wall time.
"""

import hashlib
import json
from collections import Counter, defaultdict
from datetime import datetime
from pathlib import Path
import re
import sys
import xml.etree.ElementTree as ET

NS = {"t": "http://microsoft.com/schemas/VisualStudio/TeamTest/2010"}
ROOT = Path(__file__).resolve().parents[1]


def seconds(value):
    hours, minutes, remainder = value.split(":")
    return int(hours) * 3600 + int(minutes) * 60 + float(remainder)


def union_seconds(intervals):
    total = 0.0
    end = None
    for start, stop in sorted(intervals):
        if stop < start:
            raise ValueError("Negative execution interval")
        if end is None or start > end:
            total += (stop - start).total_seconds()
        elif stop > end:
            total += (stop - end).total_seconds()
        end = max(end, stop) if end is not None else stop
    return total


def summarize(path):
    data = path.read_bytes()
    root = ET.fromstring(data)
    times = root.find("t:Times", NS)
    definitions = {
        item.get("id"): item.find("t:TestMethod", NS).get("className")
        for item in root.findall("t:TestDefinitions/t:UnitTest", NS)
    }
    postgres_classes = set()
    for source in (ROOT / "src/ApexRacers.Tests").rglob("*.cs"):
        text = source.read_text()
        if "[Collection(PostgreSqlCollection.Name)]" in text:
            namespace = re.search(r"namespace ([\w.]+);", text)
            name = re.search(r"public (?:sealed )?class (\w+)", text)
            if namespace and name:
                postgres_classes.add(namespace[1] + "." + name[1])
    classes = defaultdict(lambda: {"count": 0, "sum_seconds": 0.0})
    cases = []
    intervals = []
    postgres_intervals = []
    for result in root.findall("t:Results/t:UnitTestResult", NS):
        name = definitions[result.get("testId")]
        duration = seconds(result.get("duration"))
        interval = tuple(datetime.fromisoformat(result.get(key)) for key in ("startTime", "endTime"))
        intervals.append(interval)
        if name in postgres_classes:
            postgres_intervals.append(interval)
        classes[name]["count"] += 1
        classes[name]["sum_seconds"] += duration
        cases.append({"name": result.get("testName"), "seconds": duration, "outcome": result.get("outcome")})
    if not cases or times is None:
        raise ValueError(f"No timed results: {path}")
    elapsed = (datetime.fromisoformat(times.get("finish")) - datetime.fromisoformat(times.get("start"))).total_seconds()
    return {
        "file": str(path),
        "sha256": hashlib.sha256(data).hexdigest(),
        "count": len(cases),
        "outcomes": dict(Counter(case["outcome"] for case in cases)),
        "inventory_sha256": hashlib.sha256("\n".join(sorted(case["name"] for case in cases)).encode()).hexdigest(),
        "run_seconds": elapsed,
        "test_interval_union_seconds": union_seconds(intervals),
        "postgres_count": sum(classes[name]["count"] for name in postgres_classes),
        "postgres_interval_union_seconds": union_seconds(postgres_intervals),
        "postgres_span_seconds": (max(stop for _, stop in postgres_intervals) - min(start for start, _ in postgres_intervals)).total_seconds() if postgres_intervals else 0,
        "classes": [dict(name=name, postgres=name in postgres_classes, **value) for name, value in sorted(classes.items(), key=lambda item: -item[1]["sum_seconds"])],
        "slowest_cases": sorted(cases, key=lambda case: -case["seconds"])[:20],
    }


if __name__ == "__main__":
    if len(sys.argv) < 2:
        sys.exit(__doc__)
    print(json.dumps([summarize(Path(argument)) for argument in sys.argv[1:]], indent=2))
