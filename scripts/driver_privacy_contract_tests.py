import contextlib
import io
import importlib.util
import json
import pathlib
import tempfile
import unittest
from unittest import mock

spec = importlib.util.spec_from_file_location("privacy_contract", pathlib.Path(__file__).with_name("driver-privacy-contract.py"))
contract = importlib.util.module_from_spec(spec)
spec.loader.exec_module(contract)


class EvidenceBoundaryTests(unittest.TestCase):
    def row(self):
        return {"id": "AUTH-01", "backend": ["Tests.Owner.Allowed"], "browser": ["owner view"], "notExercised": ["Live proof"]}

    def test_a_missing_case_cannot_be_inherited_from_other_passing_cases(self):
        actual = contract.row_result(self.row(), [{"case": "Tests.Owner.Other", "result": "Passed"}], [{"case": "owner view", "result": "Passed"}])
        self.assertEqual("not exercised", actual["result"])

    def test_failed_original_case_cannot_disappear_behind_a_passing_duplicate(self):
        actual = contract.row_result(self.row(), [{"case": "Tests.Owner.Allowed", "result": "Failed"}, {"case": "Tests.Owner.Allowed", "result": "Passed"}], [{"case": "owner view", "result": "Passed"}])
        self.assertEqual("failed", actual["result"])

    def test_bounded_success_never_certifies_unexercised_live_requirements(self):
        actual = contract.row_result(self.row(), [{"case": "Tests.Owner.Allowed", "result": "Passed"}], [{"case": "owner view", "result": "Passed"}])
        self.assertEqual("passed", actual["result"])
        self.assertEqual("not exercised", actual["fullRequirementResult"])

    def test_browser_retry_is_a_failure_even_when_playwright_calls_it_expected(self):
        report = {"stats": {"startTime": "2026-10-10T00:00:00Z"}, "suites": [{"specs": [{"title": "owner view", "tests": [{"status": "expected", "results": [{"status": "failed", "retry": 0}, {"status": "passed", "retry": 1}]}]}]}]}
        with tempfile.TemporaryDirectory() as directory:
            path = pathlib.Path(directory) / "browser.json"
            path.write_text(json.dumps(report))
            results, _ = contract.browser_results(path)
        self.assertEqual("Failed", results[0]["result"])

    def test_browser_infrastructure_error_cannot_pass_a_positive_case(self):
        with tempfile.TemporaryDirectory() as directory:
            path = pathlib.Path(directory) / "browser.json"
            path.write_text(json.dumps({"errors": [{"message": "fixture failed"}], "suites": []}))
            with self.assertRaises(ValueError):
                contract.browser_results(path)

    def test_empty_backend_report_cannot_pass(self):
        with tempfile.TemporaryDirectory() as directory:
            path = pathlib.Path(directory) / "backend.trx"
            path.write_text('<TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010"><ResultSummary><Counters total="0"/></ResultSummary></TestRun>')
            with self.assertRaises(ValueError):
                contract.backend_results(path)

    def test_missing_admitted_output_cannot_be_replaced_by_a_controlled_review_label(self):
        with tempfile.TemporaryDirectory() as directory:
            with self.assertRaisesRegex(ValueError, "actually admitted"):
                contract.validate_admissions(pathlib.Path(directory), contract.instant("2026-10-10T00:00:00Z"))

    def test_missing_execution_context_still_emits_every_matrix_row_as_unexercised(self):
        with tempfile.TemporaryDirectory() as directory:
            path = pathlib.Path(directory)
            with contextlib.redirect_stdout(io.StringIO()):
                code = contract.collect(path, 1, 1)
            manifest = json.loads((path / "manifest.json").read_text())
        self.assertEqual(1, code)
        self.assertEqual(43, len(manifest["rows"]))
        self.assertEqual({"not exercised"}, {row["result"] for row in manifest["rows"]})
        self.assertFalse(manifest["liveEligible"])

    def test_ambiguous_or_malformed_timestamps_cannot_establish_freshness(self):
        for value in [123, None, "2026-10-10T00:00:00", "unknown"]:
            with self.assertRaises(ValueError):
                contract.instant(value)

    def test_replaced_compiled_artifact_cannot_retain_original_inventory(self):
        context = {"compiledReleaseAssemblies": {"ApexRacers.Api.dll": "original"}, "compiledReleaseConfiguration": {"ApexRacers.Api.runtimeconfig.json": "original"}, "builtSpa": {"index.html": "original"}}
        for changed in context:
            current = {name: dict(inventory) for name, inventory in context.items()}
            current[changed][next(iter(current[changed]))] = "replacement"
            with mock.patch.object(contract, "build_inventories", return_value=current):
                with self.assertRaisesRegex(ValueError, "inventory changed"):
                    contract.validate_builds(context)

    def test_missing_builds_cannot_establish_execution_binding(self):
        with tempfile.TemporaryDirectory() as directory:
            with mock.patch.object(contract, "ROOT", pathlib.Path(directory)):
                with self.assertRaisesRegex(ValueError, "Complete Release"):
                    contract.build_inventories()

    def test_changed_matrix_requirement_cannot_inherit_an_unchanged_id(self):
        with tempfile.TemporaryDirectory() as directory:
            root = pathlib.Path(directory)
            matrix = root / "matrix.md"
            matrix.write_text("| AUTH-01 | original requirement |")
            mapping = {"matrixSource": "matrix.md", "matrixSourceSha256": contract.digest(matrix)}
            with mock.patch.object(contract, "ROOT", root):
                contract.validate_matrix_source(mapping)
                matrix.write_text("| AUTH-01 | changed requirement |")
                with self.assertRaisesRegex(ValueError, "requirements changed"):
                    contract.validate_matrix_source(mapping)

    def test_finalized_failure_cannot_be_replaced_by_a_later_collector_success(self):
        with tempfile.TemporaryDirectory() as directory:
            path = pathlib.Path(directory)
            original = '{"syntheticGate":"failed","originalFailure":"unauthorized disclosure"}'
            (path / "manifest.json").write_text(original)
            with self.assertRaisesRegex(ValueError, "cannot be overwritten"):
                contract.collect(path, 0, 0)
            self.assertEqual(original, (path / "manifest.json").read_text())


if __name__ == "__main__":
    unittest.main()
