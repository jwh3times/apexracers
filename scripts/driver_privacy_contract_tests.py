import importlib.util
import json
import pathlib
import tempfile
import unittest

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


if __name__ == "__main__":
    unittest.main()
