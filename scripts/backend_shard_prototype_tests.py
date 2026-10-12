"""Synthetic evidence tests for #405 rejection paths; never substitute for backend runs."""
import copy
import importlib.util
import json
from pathlib import Path
import tempfile
import unittest
import xml.etree.ElementTree as ET
from unittest.mock import patch

spec = importlib.util.spec_from_file_location('prototype', Path(__file__).with_name('backend-shard-prototype.py'))
m = importlib.util.module_from_spec(spec)
spec.loader.exec_module(m)


class EvidenceTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(); self.root = Path(self.temp.name)
        self.rows = [{'uid': str(i), 'displayName': f'Example.Class{i}.Theory(value: {i})',
                      'type': {'namespace': 'Example', 'typeName': f'Class{i}'}} for i in range(2)]
        self.plan = {'runId': 'original-run', 'sourceHead': 'head', 'sdk': '10.0.test', 'sdkFingerprint': 'sdk-bytes',
                     'buildInventory': {'product.dll': 'bytes'}, 'discovery': {'tests': self.rows},
                     'shards': [[row] for row in self.rows]}
        self.plan['buildSha256'] = m.canonical(self.plan['buildInventory'])
        self.plan['discoverySha256'] = m.canonical(self.plan['discovery'])
        self.bind_plan()
        self.dirs = [self.root / str(i) for i in range(2)]
        for i, directory in enumerate(self.dirs):
            directory.mkdir(); (directory / 'original.log').write_text('Original invocation\n')
            (directory / 'original.coverage').write_bytes(b'Original native coverage bytes')
            (directory / 'coverage-export-original.log').write_text('18.12.0 export\n')
            self.trx(i, [(self.rows[i]['displayName'], 'Passed')]); self.coverage(directory / 'coverage.cobertura.xml')
            m.write(directory / 'filtered-discovery-original.json', {'tests': [self.rows[i]]})
            m.write(directory / 'cleanup.json', {'remainingProcesses': [], 'additionalContainers': []})
            m.write(directory / 'command.json', ['original']); m.write(directory / 'docker-before.json', [])
            self.producer(i)

    def tearDown(self): self.temp.cleanup()

    def bind_plan(self):
        self.plan.pop('planSha256', None); self.plan['planSha256'] = m.canonical(self.plan)

    def trx(self, index, cases, duplicate_id=False):
        root = ET.Element('{' + m.NS['t'] + '}TestRun'); results = ET.SubElement(root, '{' + m.NS['t'] + '}Results')
        for i, (name, outcome) in enumerate(cases):
            ET.SubElement(results, '{' + m.NS['t'] + '}UnitTestResult', testName=name, outcome=outcome,
                          duration='00:00:01', testId=str(0 if duplicate_id else i), executionId=str(i))
        ET.ElementTree(root).write(self.dirs[index] / 'original.trx')

    def coverage(self, path, **attrs):
        values = {'lines-valid': '10', 'lines-covered': '9', 'branches-valid': '2', 'branches-covered': '2',
                  'line-rate': '.9', 'branch-rate': '1'}; values.update(attrs)
        root = ET.Element('coverage', values); packages = ET.SubElement(root, 'packages')
        for name in sorted(m.PRODUCT_MODULES):
            package = ET.SubElement(packages, 'package', name=name); classes = ET.SubElement(package, 'classes')
            cls = ET.SubElement(classes, 'class', name=name + '.Example', filename='src/' + name + '/Example.cs')
            lines = ET.SubElement(cls, 'lines'); ET.SubElement(lines, 'line', number='1', hits='1', branch='false')
        ET.ElementTree(root).write(path)

    def producer(self, index, **updates):
        directory = self.dirs[index]
        report = {'runId': self.plan['runId'], 'producer': f'shard-{index + 1}', 'sourceHead': self.plan['sourceHead'],
                  'planSha256': self.plan['planSha256'], 'buildSha256': self.plan['buildSha256'],
                  'sdk': self.plan['sdk'], 'sdkFingerprint': self.plan['sdkFingerprint'], 'coverageTool': '18.12.0+commit',
                  'status': 'success', 'exitCode': 0, 'inventoryBefore': True, 'inventoryAfter': True,
                  'filteredDiscoveryMatches': True, 'cleanupVerified': True, 'cancelled': False,
                  'originalArtifacts': {str(path.relative_to(directory)): m.sha(path) for path in directory.rglob('*')
                                        if path.is_file() and path.name != 'producer.json'}}
        report.update(updates); m.write(directory / 'producer.json', report)

    def valid(self, statuses=None): m.validate_producers(self.plan, self.dirs, statuses or ['success', 'success'])

    def test_success_requires_complete_disjoint_original_inventory(self): self.valid()

    def test_prerequisite_failure_cancel_skip_missing_rejected(self):
        for state in ('failure', 'cancelled', 'skipped', 'missing'):
            with self.subTest(state=state), self.assertRaises(ValueError): self.valid(['success', state])

    def test_missing_producer_and_missing_native_original_rejected(self):
        with self.assertRaises(ValueError): m.validate_producers(self.plan, self.dirs[:1], ['success', 'success'])
        (self.dirs[1] / 'original.coverage').unlink()
        with self.assertRaises(ValueError): self.valid()

    def test_missing_duplicate_skipped_inconclusive_failed_stale_cases_rejected(self):
        name = self.rows[1]['displayName']
        for cases in ([], [(name, 'Passed')] * 2, [(name, 'NotExecuted')], [(name, 'Inconclusive')],
                      [(name, 'Failed')], [('later substituted case', 'Passed')]):
            with self.subTest(cases=cases):
                self.trx(1, cases); self.producer(1)
                with self.assertRaises(ValueError): self.valid()

    def test_duplicate_trx_execution_identity_rejected(self):
        self.trx(1, [('theory one', 'Passed'), ('theory two', 'Passed')], duplicate_id=True)
        with self.assertRaises(ValueError): m.results(self.dirs[1] / 'original.trx')

    def test_stale_bindings_nonzero_cancellation_and_cleanup_rejected(self):
        for changes in ({'runId': 'stale'}, {'sourceHead': 'stale'}, {'buildSha256': 'stale'},
                        {'planSha256': 'stale'}, {'sdkFingerprint': 'stale'}, {'exitCode': 1},
                        {'cancelled': True}, {'status': 'cancelled'}, {'inventoryAfter': False},
                        {'cleanupVerified': False}, {'filteredDiscoveryMatches': False}, {'coverageTool': '18.11.0'}):
            with self.subTest(changes=changes):
                self.producer(1, **changes)
                with self.assertRaises(ValueError): self.valid()

    def test_changed_original_and_removed_fault_evidence_rejected(self):
        (self.dirs[1] / 'original.log').write_text('Replaced original\n')
        with self.assertRaises(ValueError): self.valid()
        self.producer(1); fault = self.dirs[1] / 'fault.json'; m.write(fault, {'failed': True}); self.producer(1)
        fault.unlink()
        with self.assertRaises(ValueError): self.valid()

    def test_duplicate_shard_uid_and_whole_class_split_rejected(self):
        self.plan['shards'][1] = self.plan['shards'][0]; self.bind_plan()
        with self.assertRaises(ValueError): m.verify_plan(self.plan)
        self.rows[1]['type'] = self.rows[0]['type']; self.plan['shards'] = [[row] for row in self.rows]
        self.plan['discoverySha256'] = m.canonical(self.plan['discovery']); self.bind_plan()
        with self.assertRaises(ValueError): m.verify_plan(self.plan)

    def test_ambiguous_theory_names_and_empty_discovery_rejected(self):
        ambiguous = copy.deepcopy(self.rows); ambiguous[1]['displayName'] = ambiguous[0]['displayName']
        for rows in (ambiguous, []):
            with self.subTest(rows=rows), self.assertRaises(ValueError): m.discovery_rows({'tests': rows})

    def test_empty_low_nonfinite_inconsistent_coverage_rejected(self):
        path = self.root / 'coverage.xml'
        for attrs in ({'lines-valid': '0'}, {'line-rate': '.8', 'lines-covered': '8'},
                      {'branch-rate': '.5', 'branches-covered': '1'}, {'line-rate': 'nan'},
                      {'branch-rate': 'inf'}, {'line-rate': '1.1'}, {'line-rate': '.99'},
                      {'branches-covered': '3'}):
            with self.subTest(attrs=attrs):
                self.coverage(path, **attrs)
                with self.assertRaises(ValueError): m.coverage_gate(path)

    def test_missing_product_assembly_rejected(self):
        path = self.dirs[0] / 'coverage.cobertura.xml'; root = ET.parse(path).getroot()
        packages = root.find('packages'); packages.remove(packages[0]); ET.ElementTree(root).write(path)
        with self.assertRaises(ValueError): m.coverage_gate(path)

    def test_different_source_line_or_branch_denominator_models_rejected(self):
        first = self.dirs[0] / 'coverage.cobertura.xml'; second = self.dirs[1] / 'coverage.cobertura.xml'
        self.coverage(second, **{'branches-valid': '3', 'branches-covered': '3'})
        self.assertNotEqual(m.coverage_model(first), m.coverage_model(second))
        self.coverage(second); root = ET.parse(second).getroot(); root.find('packages/package/classes/class/lines/line').set('number', '2')
        ET.ElementTree(root).write(second); self.assertNotEqual(m.coverage_model(first), m.coverage_model(second))

    def test_probe_failure_and_hidden_cleanup_leftover_rejected(self):
        (self.dirs[1] / 'original.log').write_text('FIXTURE_COST_PROBE_INVALID original fault\n'); self.producer(1)
        with self.assertRaises(ValueError): self.valid()
        (self.dirs[1] / 'original.log').write_text('Original\n')
        m.write(self.dirs[1] / 'cleanup.json', {'remainingProcesses': [{'pid': 1}], 'additionalContainers': []}); self.producer(1)
        with self.assertRaises(ValueError): self.valid()


if __name__ == '__main__': unittest.main()
