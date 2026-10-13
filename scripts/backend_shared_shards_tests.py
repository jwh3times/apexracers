"""Reject incomplete experiments and scope automatic cleanup to owned identities."""
import copy
import importlib.util
import json
import os
from pathlib import Path
import signal
import subprocess
import sys
import tempfile
import time
import unittest
import xml.etree.ElementTree as ET
from unittest.mock import patch

spec = importlib.util.spec_from_file_location('runner', Path(__file__).with_name('backend-shared-shards.py'))
runner = importlib.util.module_from_spec(spec)
spec.loader.exec_module(runner)


def row(uid, kind, value=0):
    return {'uid': uid, 'displayName': f'{kind}.Theory({value})',
            'type': {'namespace': 'Tests', 'typeName': kind}}


class CleanupJournalTests(unittest.TestCase):
    def test_followup_cleanup_keeps_original_journals_byte_for_byte(self):
        with tempfile.TemporaryDirectory() as temp:
            base = Path(temp)
            command_journal = base / 'cleanup-command-original.jsonl'
            resource_journal = base / 'cleanup-resource-bindings-original.jsonl'
            command_journal.write_text('{"original": "command"}\n')
            resource_journal.write_text('{"original": "resource"}\n')
            originals = {p: p.read_bytes() for p in (command_journal, resource_journal)}
            # The actual follow-up call site names its observation output this way.
            followup = base / 'cleanup-followup'
            followup.mkdir()
            owner = runner.Ownership([], followup / 'observations.jsonl', cleanup_phase='followup')
            owner.sessions.add('11111111-1111-1111-1111-111111111111')
            with patch.object(owner, 'observe'), patch.object(owner, 'alive', return_value={}), patch.object(runner.subprocess, 'run', side_effect=lambda argv, **kw: subprocess.CompletedProcess(argv, 0, '', '')):
                self.assertTrue(owner.cleanup(grace=0)['complete'])
            for path, before in originals.items():
                self.assertEqual(path.read_bytes(), before, path.name)


class FollowupContextTests(unittest.TestCase):
    def fixture(self, base):
        session = '11111111-1111-1111-1111-111111111111'
        ownership = base / 'ownership-original.jsonl'
        ownership.write_text(json.dumps({'identities': {}, 'sessions': [session], 'containers': []}) + '\n')
        for name in ('cleanup-command-original.jsonl', 'cleanup-resource-bindings-original.jsonl'):
            (base / name).write_text('{"original": true}\n')
        original_artifacts = {p.name: runner.product.cost.digest(p) for p in base.iterdir()}
        context = base / 'context.json'
        context.write_text(json.dumps({'producerDirectories': [], 'originalArtifacts': original_artifacts}))
        return context, {p: p.read_bytes() for p in base.iterdir()}, session

    def test_actual_followup_preserves_context_and_all_hash_bound_originals(self):
        with tempfile.TemporaryDirectory() as temp:
            base = Path(temp)
            context, originals, session = self.fixture(base)
            network = {'Id': 'network-id', 'Name': 'owned-network', 'Created': 'original-time',
                       'Labels': {'org.testcontainers.resource-reaper-session': session}}
            present = True
            def command(argv, **kwargs):
                nonlocal present
                text = ''
                if argv[:3] == ['docker', 'network', 'ls'] and present:
                    text = 'network-id\n'
                elif argv[:3] == ['docker', 'network', 'inspect']:
                    text = json.dumps([network])
                elif argv[:3] == ['docker', 'network', 'rm']:
                    present = False
                return subprocess.CompletedProcess(argv, 0, text, '')
            with patch.object(runner.Ownership, 'observe'), patch.object(runner.Ownership, 'alive', return_value={}), patch.object(runner.subprocess, 'run', side_effect=command):
                result = runner.cleanup_context(context)
            self.assertTrue(result['complete'])
            for path, before in originals.items():
                self.assertEqual(path.read_bytes(), before, path.name)
            followup = base / 'cleanup-followup'
            self.assertIn('network-id', (followup / 'cleanup-command-followup.jsonl').read_text())
            self.assertEqual(json.loads((followup / 'cleanup-resource-bindings-followup.jsonl').read_text())['id'], 'network-id')
            result_bytes = (followup / 'result.json').read_bytes()
            with self.assertRaises(FileExistsError):
                runner.cleanup_context(context)
            self.assertEqual((followup / 'result.json').read_bytes(), result_bytes)

    def test_followup_artifacts_started_before_finalization_never_enter_original_manifest(self):
        with tempfile.TemporaryDirectory() as temp:
            base = Path(temp)
            context, _, _ = self.fixture(base)
            followup = base / 'cleanup-followup'
            followup.mkdir()
            journal = followup / 'cleanup-command-followup.jsonl'
            journal.write_text('{"stage": "started"}\n')
            before = runner.original_artifact_hashes(base)
            journal.write_text(journal.read_text() + '{"stage": "completed"}\n')
            (followup / 'result.json').write_text('{"complete": false}')
            self.assertEqual(runner.original_artifact_hashes(base), before)
            self.assertEqual(before, json.loads(context.read_text())['originalArtifacts'])

    def test_followup_binds_the_consumed_context_during_parent_finalization(self):
        with tempfile.TemporaryDirectory() as temp:
            base = Path(temp)
            context, _, _ = self.fixture(base)
            consumed = context.read_bytes()
            original_mkdir = Path.mkdir
            def replace_context(path, *args, **kwargs):
                original_mkdir(path, *args, **kwargs)
                if path.name == 'cleanup-followup':
                    context.write_text('{"producerDirectories": [], "finalized": true}')
            with patch.object(Path, 'mkdir', replace_context), patch.object(runner.Ownership, 'cleanup', return_value={'complete': True}):
                runner.cleanup_context(context)
            started = json.loads((base / 'cleanup-followup/started.json').read_text())
            self.assertEqual(started['sourceContextSha256'], runner.hashlib.sha256(consumed).hexdigest())
            self.assertEqual((base / 'cleanup-followup/source-context.json').read_bytes(), consumed)

    def test_followup_nonzero_command_is_retained_without_changing_originals(self):
        with tempfile.TemporaryDirectory() as temp:
            base = Path(temp)
            context, originals, _ = self.fixture(base)
            with patch.object(runner.Ownership, 'observe'), patch.object(runner.Ownership, 'alive', return_value={}), patch.object(runner.subprocess, 'run', side_effect=lambda argv, **kw: subprocess.CompletedProcess(argv, 2, '', 'daemon unavailable')):
                result = runner.cleanup_context(context)
            self.assertFalse(result['complete'])
            entries = [json.loads(line) for line in (base / 'cleanup-followup/cleanup-command-followup.jsonl').read_text().splitlines()]
            self.assertEqual(entries[-1]['stage'], 'completed')
            self.assertEqual(entries[-1]['exitCode'], 2)
            for path, before in originals.items():
                self.assertEqual(path.read_bytes(), before, path.name)

    def test_followup_timeout_is_durable_and_never_rewrites_originals(self):
        with tempfile.TemporaryDirectory() as temp:
            base = Path(temp)
            context, originals, _ = self.fixture(base)
            def timeout(argv, **kwargs):
                raise subprocess.TimeoutExpired(argv, 1)
            with patch.object(runner.Ownership, 'observe'), patch.object(runner.Ownership, 'alive', return_value={}), patch.object(runner.subprocess, 'run', side_effect=timeout):
                result = runner.cleanup_context(context)
            self.assertFalse(result['complete'])
            self.assertEqual(result['errorType'], 'TimeoutExpired')
            entries = [json.loads(line) for line in (base / 'cleanup-followup/cleanup-command-followup.jsonl').read_text().splitlines()]
            self.assertEqual([x['stage'] for x in entries], ['started', 'failed'])
            self.assertEqual(entries[-1]['errorType'], 'TimeoutExpired')
            for path, before in originals.items():
                self.assertEqual(path.read_bytes(), before, path.name)
            self.assertFalse(json.loads((base / 'cleanup-followup/result.json').read_text())['complete'])


class InventoryTests(unittest.TestCase):
    def test_theory_cases_partitioned_once_as_whole_classes(self):
        rows = [row('a', 'A'), row('b', 'A', 1), row('c', 'B')]
        runner.verify_partition(rows, [rows[:2], rows[2:]])
        changed = copy.deepcopy(rows)
        changed[0]['displayName'] = 'Changed'
        bad = [[rows[:1], rows[1:]], [rows, rows[2:]], [rows[:2], []],
               [[changed[0], rows[1]], rows[2:]], [rows[:1], rows[2:]]]
        for shards in bad:
            with self.subTest(shards=shards), self.assertRaises(ValueError):
                runner.verify_partition(rows, shards)

    def test_copy_mapping_binds_theory_metadata_not_location_uid(self):
        rows = [row('a', 'A'), row('b', 'B')]
        copied = [dict(r, uid='copy-' + r['uid']) for r in rows]
        self.assertEqual(runner.shared.map_discovery(rows, copied), {'A.Theory(0)': 'copy-a', 'B.Theory(0)': 'copy-b'})
        copied[0]['type'] = {'namespace': 'Other', 'typeName': 'A'}
        with self.assertRaises(ValueError):
            runner.shared.map_discovery(rows, copied)


class OriginalResultsTests(unittest.TestCase):
    def write_trx(self, directory, rows):
        ns = 'http://microsoft.com/schemas/VisualStudio/TeamTest/2010'
        root = ET.Element('{' + ns + '}TestRun')
        results = ET.SubElement(root, '{' + ns + '}Results')
        for name, outcome in rows:
            ET.SubElement(results, '{' + ns + '}UnitTestResult', testName=name, outcome=outcome)
        ET.ElementTree(root).write(directory / 'original.trx')

    def test_exact_passing_theory_execution_inventory_and_exit_required(self):
        with tempfile.TemporaryDirectory() as temp:
            directory = Path(temp)
            cases = [row('a', 'A'), row('b', 'A', 1)]
            expected = [(c['displayName'], 'Passed') for c in cases]
            (directory / 'original-exit.json').write_text('{"exitCode": 0}')
            self.write_trx(directory, expected)
            runner.validate_original_results(directory, cases)
            invalid = [[], expected[:1], expected + expected[:1],
                       [(expected[0][0], 'Passed'), (expected[1][0], 'Skipped')],
                       [(expected[0][0], 'Passed'), (expected[1][0], 'Inconclusive')],
                       [(expected[0][0], 'Passed'), (expected[1][0], 'Failed')],
                       [expected[0], ('Extra.Theory(0)', 'Passed')]]
            for rows in invalid:
                self.write_trx(directory, rows)
                with self.subTest(rows=rows), self.assertRaises(ValueError):
                    runner.validate_original_results(directory, cases)
            self.write_trx(directory, expected)
            (directory / 'original-exit.json').write_text('{"exitCode": 1}')
            with self.assertRaises(ValueError):
                runner.validate_original_results(directory, cases)
            (directory / 'original-exit.json').unlink()
            with self.assertRaises(FileNotFoundError):
                runner.validate_original_results(directory, cases)
            (directory / 'original-exit.json').write_text('{"exitCode": 0}')
            (directory / 'original.trx').unlink()
            with self.assertRaises(FileNotFoundError):
                runner.validate_original_results(directory, cases)


class ProbeEvidenceTests(unittest.TestCase):
    def test_invalid_instrumentation_cannot_report_complete_cleanup(self):
        with tempfile.TemporaryDirectory() as temp:
            directory = Path(temp)
            (directory / 'original.log').write_text('FIXTURE_COST_PROBE_INVALID IOException')
            with self.assertRaises(ValueError):
                runner.validate_probe_evidence(directory, [])

    def test_pg_requires_session_ready_and_success_disposal(self):
        with tempfile.TemporaryDirectory() as temp:
            directory = Path(temp)
            source = directory / 'Case.cs'
            source.write_text('PostgreSqlFixture')
            cases = [{'location': {'file': str(source)}}]
            phases = directory / 'phases'
            phases.mkdir()
            file = phases / 'events.jsonl'
            session = {'kind': 'testcontainers-session', 'id': 'session', 'processId': 42}
            ready = {'kind': 'container', 'id': 'container', 'processId': 42, 'lifecycle': 'ready'}
            for events in ([], [ready], [session], [session, ready]):
                file.write_text(''.join(json.dumps(e) + '\n' for e in events))
                with self.assertRaises(ValueError):
                    runner.validate_probe_evidence(directory, cases)
            runner.validate_probe_evidence(directory, cases, complete=False)
            file.write_text(''.join(json.dumps(e) + '\n' for e in [session, ready, dict(ready, lifecycle='disposed')]))
            runner.validate_probe_evidence(directory, cases)


class ProcessOwnershipTests(unittest.TestCase):
    def test_orphans_retained_without_live_root_but_pid_reuse_rejected(self):
        rows = {20: {'parentPid': 1, 'startTimeTicks': 200},
                30: {'parentPid': 20, 'startTimeTicks': 300},
                40: {'parentPid': 1, 'startTimeTicks': 401},
                50: {'parentPid': 1, 'startTimeTicks': 500}}
        self.assertEqual(runner.select_descendants(rows, {10: 100, 20: 200, 40: 400}), {20: 200, 30: 300})

    def test_socket_pid_and_start_ticks_must_both_match(self):
        with tempfile.TemporaryDirectory() as temp:
            base = Path(temp)
            for name in ('dotnet-diagnostic-20-200-socket', 'dotnet-diagnostic-30-299-socket',
                         'dotnet-diagnostic-40-400-socket', 'dotnet-diagnostic-bad-socket'):
                (base / name).touch()
            rows = {20: {'startTimeTicks': 200}, 30: {'startTimeTicks': 300}}
            self.assertEqual(runner.diagnostic_identities(base, rows), {20: 200})

    def test_termination_never_signals_unrelated_pid(self):
        with tempfile.TemporaryDirectory() as temp:
            owner = runner.Ownership([], Path(temp) / 'observations')
            with patch.object(owner, 'alive', return_value={20: 200}), patch.object(owner, 'signal_identity') as kill:
                owner.terminate(signal.SIGTERM)
            kill.assert_called_once_with(20, 200, signal.SIGTERM)

    def test_cleanup_does_not_remove_unowned_docker_delta(self):
        with tempfile.TemporaryDirectory() as temp:
            owner = runner.Ownership([], Path(temp) / 'observations')
            with patch.object(owner, 'observe'), patch.object(owner, 'alive', return_value={}), patch.object(runner.subprocess, 'run') as run:
                result = owner.cleanup(grace=0)
            self.assertTrue(result['complete'])
            run.assert_not_called()

    def test_changed_container_creation_identity_rejects_removal(self):
        with tempfile.TemporaryDirectory() as temp:
            owner = runner.Ownership([], Path(temp) / 'observations')
            session = '11111111-1111-1111-1111-111111111111'
            owner.containers['owned'] = {'id': 'owned', 'created': 'before', 'session': session,
                                         'reaper': False, 'image': 'postgres:18.0-alpine'}
            data = {'Created': 'after', 'Config': {'Labels': {'org.testcontainers.resource-reaper-session': session}}}
            result = subprocess.CompletedProcess([], 0, json.dumps([data]), '')
            with patch.object(owner, 'observe'), patch.object(owner, 'alive', return_value={}), patch.object(runner.subprocess, 'run', return_value=result) as run:
                with self.assertRaises(ValueError):
                    owner.cleanup(grace=0)
            self.assertEqual(run.call_count, 1)

    def test_late_server_created_container_fails_final_absence_observation(self):
        with tempfile.TemporaryDirectory() as temp:
            owner = runner.Ownership([], Path(temp) / 'observations')
            owner.sessions.add('11111111-1111-1111-1111-111111111111')
            def command(argv, **kwargs):
                output = 'late-container\n' if argv[:3] == ['docker', 'ps', '-aq'] and 'label=' in argv[-1] else ''
                return subprocess.CompletedProcess(argv, 0, output, '')
            with patch.object(owner, 'observe'), patch.object(owner, 'alive', return_value={}), patch.object(runner.subprocess, 'run', side_effect=command) as run:
                result = owner.cleanup(grace=0)
            self.assertFalse(result['complete'])
            self.assertIn('late-container', result['remainingContainers'])
            self.assertEqual(len(result['finalObservations']), 8)
            self.assertFalse(any(call.args[0][:3] == ['docker', 'rm', '-f'] for call in run.call_args_list))

    def test_volume_without_immutable_id_is_recorded_and_never_removed_by_name(self):
        with tempfile.TemporaryDirectory() as temp:
            owner = runner.Ownership([], Path(temp) / 'observations')
            session = '11111111-1111-1111-1111-111111111111'
            owner.sessions.add(session)
            def command(argv, **kwargs):
                if argv[:3] == ['docker', 'volume', 'ls']:
                    return subprocess.CompletedProcess(argv, 0, 'reusable-volume\n', '')
                if argv[:3] == ['docker', 'volume', 'inspect']:
                    data = {'Name': 'reusable-volume', 'CreatedAt': 'original-time',
                            'Labels': {'org.testcontainers.resource-reaper-session': session}}
                    return subprocess.CompletedProcess(argv, 0, json.dumps([data]), '')
                return subprocess.CompletedProcess(argv, 0, '', '')
            with patch.object(owner, 'observe'), patch.object(owner, 'alive', return_value={}), patch.object(runner.subprocess, 'run', side_effect=command) as run:
                result = owner.cleanup(grace=0)
            self.assertFalse(result['complete'])
            self.assertEqual(result['resourceBindings'][0]['created'], 'original-time')
            self.assertFalse(any(call.args[0][:3] == ['docker', 'volume', 'rm'] for call in run.call_args_list))

    def test_network_creation_identity_rechecked_and_changed_generation_never_removed(self):
        with tempfile.TemporaryDirectory() as temp:
            owner = runner.Ownership([], Path(temp) / 'observations')
            session = '11111111-1111-1111-1111-111111111111'
            owner.sessions.add(session)
            inspections = 0
            def command(argv, **kwargs):
                nonlocal inspections
                if argv[:3] == ['docker', 'network', 'ls']:
                    return subprocess.CompletedProcess(argv, 0, 'network-id\n', '')
                if argv[:3] == ['docker', 'network', 'inspect']:
                    inspections += 1
                    data = {'Name': 'network', 'Id': 'network-id', 'Created': 'before' if inspections == 1 else 'after',
                            'Labels': {'org.testcontainers.resource-reaper-session': session}}
                    return subprocess.CompletedProcess(argv, 0, json.dumps([data]), '')
                return subprocess.CompletedProcess(argv, 0, '', '')
            with patch.object(owner, 'observe'), patch.object(owner, 'alive', return_value={}), patch.object(runner.subprocess, 'run', side_effect=command) as run:
                with self.assertRaises(ValueError):
                    owner.cleanup(grace=0)
            self.assertFalse(any(call.args[0][:3] == ['docker', 'network', 'rm'] for call in run.call_args_list))

    def test_unlabelled_mounted_volume_survivor_fails_without_name_removal(self):
        with tempfile.TemporaryDirectory() as temp:
            owner = runner.Ownership([], Path(temp) / 'observations')
            owner.containers['removed-container'] = {'id': 'removed-container', 'created': 'before', 'session': 'session',
                'reaper': False, 'image': 'postgres', 'volumeNames': ['anonymous-volume']}
            def command(argv, **kwargs):
                if argv[:2] == ['docker', 'inspect']:
                    return subprocess.CompletedProcess(argv, 1, '', 'error: no such object: removed-container')
                if argv[:3] == ['docker', 'volume', 'inspect']:
                    return subprocess.CompletedProcess(argv, 0, json.dumps([{'Name': 'anonymous-volume', 'Labels': {}}]), '')
                return subprocess.CompletedProcess(argv, 0, '', '')
            with patch.object(owner, 'observe'), patch.object(owner, 'alive', return_value={}), patch.object(runner.subprocess, 'run', side_effect=command) as run:
                result = owner.cleanup(grace=0)
            self.assertFalse(result['complete'])
            self.assertIn('anonymous-volume', result['remainingResources'])
            self.assertFalse(any(call.args[0][:3] == ['docker', 'volume', 'rm'] for call in run.call_args_list))

    def test_verified_owned_container_removal_includes_anonymous_volumes(self):
        with tempfile.TemporaryDirectory() as temp:
            owner = runner.Ownership([], Path(temp) / 'observations')
            owner.containers['container-id'] = {'id': 'container-id', 'created': 'before', 'session': 'session',
                'reaper': False, 'image': 'postgres', 'volumeNames': []}
            def command(argv, **kwargs):
                if argv[:2] == ['docker', 'inspect']:
                    data = {'Created': 'before', 'Config': {'Labels': {'org.testcontainers.resource-reaper-session': 'session'}}}
                    return subprocess.CompletedProcess(argv, 0, json.dumps([data]), '')
                return subprocess.CompletedProcess(argv, 0, '', '')
            with patch.object(owner, 'observe'), patch.object(owner, 'alive', return_value={}), patch.object(runner.subprocess, 'run', side_effect=command) as run:
                result = owner.cleanup(grace=0)
            self.assertTrue(result['complete'])
            self.assertTrue(any(call.args[0] == ['docker', 'rm', '-f', '-v', 'container-id'] for call in run.call_args_list))

    def test_concurrent_owned_removal_waits_for_two_clear_samples(self):
        with tempfile.TemporaryDirectory() as temp:
            owner = runner.Ownership([], Path(temp) / 'observations')
            owner.sessions.add('session')
            owner.containers['container-id'] = {'id': 'container-id', 'created': 'before', 'session': 'session',
                'reaper': False, 'image': 'postgres', 'volumeNames': []}
            ps = 0
            def command(argv, **kwargs):
                nonlocal ps
                if argv[:2] == ['docker', 'inspect']:
                    data = {'Created': 'before', 'Config': {'Labels': {'org.testcontainers.resource-reaper-session': 'session'}}}
                    return subprocess.CompletedProcess(argv, 0, json.dumps([data]), '')
                if argv[:2] == ['docker', 'rm']:
                    return subprocess.CompletedProcess(argv, 1, '', 'Error response from daemon: removal of container container-id is already in progress')
                if argv[:3] == ['docker', 'ps', '-aq'] and 'label=' in argv[-1]:
                    ps += 1
                    return subprocess.CompletedProcess(argv, 0, 'container-id\n' if ps == 1 else '', '')
                return subprocess.CompletedProcess(argv, 0, '', '')
            with patch.object(owner, 'observe'), patch.object(owner, 'alive', return_value={}), patch.object(runner.subprocess, 'run', side_effect=command):
                result = owner.cleanup(grace=0)
            self.assertTrue(result['complete'])
            self.assertEqual(len(result['finalObservations']), 3)
            self.assertTrue(result['finalObservations'][0]['resources'])
            self.assertEqual(result['actions'][0]['exitCode'], 1)
            self.assertEqual(result['actions'][0]['failureKind'], 'owned-removal-in-progress')

    def test_daemon_unavailable_removal_never_green_even_if_later_enumeration_empty(self):
        with tempfile.TemporaryDirectory() as temp:
            owner = runner.Ownership([], Path(temp) / 'observations')
            owner.containers['container-id'] = {'id': 'container-id', 'created': 'before', 'session': 'session',
                'reaper': False, 'image': 'postgres', 'volumeNames': []}
            def command(argv, **kwargs):
                if argv[:2] == ['docker', 'inspect']:
                    data = {'Created': 'before', 'Config': {'Labels': {'org.testcontainers.resource-reaper-session': 'session'}}}
                    return subprocess.CompletedProcess(argv, 0, json.dumps([data]), '')
                if argv[:2] == ['docker', 'rm']:
                    return subprocess.CompletedProcess(argv, 1, '', 'Cannot connect to the Docker daemon')
                return subprocess.CompletedProcess(argv, 0, '', '')
            with patch.object(owner, 'observe'), patch.object(owner, 'alive', return_value={}), patch.object(runner.subprocess, 'run', side_effect=command):
                result = owner.cleanup(grace=0)
            self.assertFalse(result['complete'])
            self.assertTrue(result['stableAbsence'])
            self.assertEqual(result['commandFailures'][0]['failureKind'], 'unclassified')


class ChildCancellationTests(unittest.TestCase):
    def test_sigterm_keeps_both_original_exit_records_and_nonzero_parent(self):
        with tempfile.TemporaryDirectory() as temp:
            base = Path(temp)
            executable = base / 'fake-test'
            executable.write_text('#!/usr/bin/env python3\nimport time\ntime.sleep(60)\n')
            executable.chmod(0o755)
            producers = []
            for index in range(2):
                directory = base / str(index)
                directory.mkdir()
                producers.append({'directory': str(directory), 'executable': str(executable), 'uids': [str(index)]})
            config = base / 'children.json'
            config.write_text(json.dumps({'producers': producers, 'fault': None, 'faultAfter': 0}))
            process = subprocess.Popen([sys.executable, str(Path(runner.__file__)), '--children', str(config)])
            try:
                deadline = time.monotonic() + 10
                while not (base / 'producer-launch-original.json').exists():
                    if process.poll() is not None or time.monotonic() > deadline:
                        self.fail('Fake producer startup failed')
                    time.sleep(.05)
                process.terminate()
                self.assertNotEqual(process.wait(timeout=5), 0)
                for item in producers:
                    record = json.loads((Path(item['directory']) / 'original-exit.json').read_text())
                    self.assertNotEqual(record['exitCode'], 0)
                    env = json.loads((Path(item['directory']) / 'environment-paths-original.json').read_text())
                    import shutil
                    shutil.rmtree(env['TMPDIR'])
            finally:
                if process.poll() is None:
                    process.kill()
                    process.wait()


if __name__ == '__main__':
    unittest.main()
