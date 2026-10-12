"""Copied MTP identities must map complete unchanged case metadata one-to-one."""
import copy
import importlib.util
from pathlib import Path
import unittest

spec = importlib.util.spec_from_file_location('shared', Path(__file__).with_name('probe-shared-product-coverage.py'))
shared = importlib.util.module_from_spec(spec)
spec.loader.exec_module(shared)


class DiscoveryMappingTests(unittest.TestCase):
    def setUp(self):
        self.original = [{'uid': 'original-' + str(i), 'displayName': f'Class.Theory(value: {i})',
                          'type': {'namespace': 'Tests', 'typeName': 'Class'}} for i in range(2)]
        self.copied = copy.deepcopy(self.original)
        for i, row in enumerate(self.copied):
            row['uid'] = 'copied-' + str(i)

    def test_location_ids_change_without_changing_theory_execution_identity(self):
        self.assertEqual(shared.map_discovery(self.original, self.copied),
                         {row['displayName']: row['uid'] for row in self.copied})

    def test_missing_added_changed_case_metadata_rejected(self):
        changed = copy.deepcopy(self.copied)
        changed[1]['type']['typeName'] = 'Other'
        for rows in (self.copied[:1], self.copied + [dict(self.copied[0], uid='extra', displayName='Extra')], changed):
            with self.subTest(rows=rows), self.assertRaises(ValueError):
                shared.map_discovery(self.original, rows)

    def test_ambiguous_uid_or_display_name_rejected(self):
        for field in ('uid', 'displayName'):
            rows = copy.deepcopy(self.copied)
            rows[1][field] = rows[0][field]
            with self.subTest(field=field), self.assertRaises(ValueError):
                shared.map_discovery(self.original, rows)


if __name__ == '__main__':
    unittest.main()
