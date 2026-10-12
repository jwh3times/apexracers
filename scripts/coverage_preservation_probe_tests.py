import copy
import importlib.util
from pathlib import Path
import unittest

spec = importlib.util.spec_from_file_location("probe", Path(__file__).with_name("probe-coverage-preservation.py"))
probe = importlib.util.module_from_spec(spec)
spec.loader.exec_module(probe)


class LiveUnionAssessmentTests(unittest.TestCase):
    def setUp(self):
        self.reports = {name: {"model": [{"class": "Decision", "file": "Program.cs", "lines": [{"number": "29", "branchDenominator": "5"}]}],
                               "attributes": {"branches-valid": "5", "branches-covered": covered}}
                        for name, covered in (("a.xml", "2"), ("b.xml", "2"), ("union.xml", "3"), ("shared-overlap.xml", "3"), ("shared-same.xml", "2"))}
        self.commands = [{"exitCode": 0}]

    def test_overlap_and_same_subsets_demonstrate_distinct_unions(self):
        self.assertTrue(probe.assess_live_union(self.reports, self.commands, True)["demonstrated"])

    def test_aggregate_guess_for_same_subset_is_rejected(self):
        self.reports["shared-same.xml"]["attributes"]["branches-covered"] = "3"
        self.assertFalse(probe.assess_live_union(self.reports, self.commands, True)["demonstrated"])

    def test_changed_branch_or_source_model_is_rejected(self):
        for field, value in (("class", "Other"), ("file", "Other.cs"), ("lines", [{"number": "30", "branchDenominator": "5"}])):
            with self.subTest(field=field):
                changed = copy.deepcopy(self.reports)
                changed["shared-overlap.xml"]["model"][0][field] = value
                self.assertFalse(probe.assess_live_union(changed, self.commands, True)["demonstrated"])

    def test_failed_producer_or_changed_inputs_reject_demonstration(self):
        self.assertFalse(probe.assess_live_union(self.reports, [{"exitCode": 1}], True)["demonstrated"])
        self.assertFalse(probe.assess_live_union(self.reports, self.commands, False)["demonstrated"])

    def test_missing_report_or_denominator_is_rejected(self):
        del self.reports["b.xml"]
        self.assertFalse(probe.assess_live_union(self.reports, self.commands, True)["demonstrated"])


if __name__ == "__main__":
    unittest.main()
