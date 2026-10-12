"""Cost-report boundary tests; these do not replace original backend/coverage evidence."""

import importlib.util
import json
from pathlib import Path
import sys
import tempfile
import unittest

sys.dont_write_bytecode = True


def load(name, filename):
    spec = importlib.util.spec_from_file_location(name, Path(__file__).with_name(filename))
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


MEASURE = load("measure_backend_cost", "measure-backend-cost.py")
ANALYZE = load("analyze_backend_cost", "analyze-backend-cost.py")


class CostTests(unittest.TestCase):
    def test_runtime_sql_and_catalog_receipt_are_fingerprinted(self):
        inventory = MEASURE.inventory()
        self.assertIn("src/ApexRacers.Data/Seeds/purge_demo_data.sql", inventory)
        self.assertIn("docs/testing/driver-catalog-admission-v1.json", inventory)

    def test_reused_root_pid_does_not_admit_unrelated_descendants(self):
        rows = {1: {"pid": 1, "parentPid": 0, "startTimeTicks": 99}, 2: {"pid": 2, "parentPid": 1, "startTimeTicks": 100}}
        self.assertEqual(MEASURE.select_owned_processes(rows, 1, {1: 10}), [])

    def test_owned_tree_rejects_reused_recorded_child(self):
        rows = {1: {"pid": 1, "parentPid": 0, "startTimeTicks": 10}, 2: {"pid": 2, "parentPid": 1, "startTimeTicks": 20}, 3: {"pid": 3, "parentPid": 0, "startTimeTicks": 99}}
        self.assertEqual([row["pid"] for row in MEASURE.select_owned_processes(rows, 1, {1: 10, 3: 30})], [1, 2])

    def test_exclusive_coverage_unions_overlapping_children(self):
        self.assertEqual(ANALYZE.union([(0, 3), (2, 5), (8, 10)]), 7)
        self.assertEqual(ANALYZE.union([(0, 10), (2, 3)]), 10)

    def test_invalid_intervals_are_rejected(self):
        with self.assertRaises(ValueError):
            ANALYZE.union([(3, 2)])

    def test_docker_units_are_numeric_only(self):
        self.assertEqual(MEASURE.memory_bytes("1.5 MiB"), 1572864)
        self.assertEqual(MEASURE.memory_bytes("2kB"), 2000)
        self.assertIsNone(MEASURE.memory_bytes("unknown"))

    def test_result_inventory_preserves_duplicate_executions_and_failures(self):
        with tempfile.TemporaryDirectory() as tmp:
            path = Path(tmp) / "original.trx"
            path.write_text('<TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010"><Results><UnitTestResult testName="theory" outcome="Passed"/><UnitTestResult testName="theory" outcome="Failed"/></Results></TestRun>')
            names, outcomes = MEASURE.read_results(path)
            self.assertEqual(names["theory"], 2)
            self.assertEqual(outcomes, {"Passed": 1, "Failed": 1})

    def test_missing_parent_is_rejected_in_original_phase_evidence(self):
        with tempfile.TemporaryDirectory() as tmp:
            path = Path(tmp)
            (path / "result.json").write_text("{}")
            (path / "phases").mkdir()
            (path / "phases/phases.jsonl").write_text(json.dumps({"eventType": "span", "processId": 1, "spanId": 2, "parentSpanId": 1}) + "\n")
            # The original TRX parser runs before phase validation; isolate that dependency.
            original = ANALYZE.TIMING.summarize
            try:
                ANALYZE.TIMING.summarize = lambda _: {}
                with self.assertRaisesRegex(ValueError, "Missing parent"):
                    ANALYZE.summarize(path)
            finally:
                ANALYZE.TIMING.summarize = original


if __name__ == "__main__":
    unittest.main()
