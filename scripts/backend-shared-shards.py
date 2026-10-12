#!/usr/bin/env python3
"""Research-only original MTP baseline or two apps in one live coverage collector.

Each output is new and immutable after completion. Failure/cancellation artifacts
are retained. This runner neither retries tests nor changes production CI.
"""
import argparse
from collections import Counter
import importlib.util
import json
import os
from pathlib import Path
import re
import shutil
import signal
import subprocess
import sys
import tempfile
import time
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[1]

def load(name, filename):
    spec = importlib.util.spec_from_file_location(name, ROOT / 'scripts' / filename)
    result = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(result)
    return result

product = load('shards_product', 'probe-product-coverage-preservation.py')
shared = load('shards_shared', 'probe-shared-product-coverage.py')


def write(path, data):
    temporary = path.with_name(path.name + '.writing')
    temporary.write_text(json.dumps(data, indent=2) + '\n')
    temporary.replace(path)


def process_rows():
    return {int(p.name): row for p in Path('/proc').iterdir() if p.name.isdigit()
            if (row := product.cost.numeric_process(int(p.name))) is not None}


def select_descendants(rows, identities):
    """Preserve observed children after their parents disappear; reject PID reuse."""
    owned = {pid for pid, ticks in identities.items()
             if pid in rows and rows[pid]['startTimeTicks'] == ticks}
    while True:
        added = {pid for pid, row in rows.items() if row['parentPid'] in owned} - owned
        if not added:
            return {pid: rows[pid]['startTimeTicks'] for pid in owned}
        owned.update(added)


def diagnostic_identities(directory, rows):
    found = {}
    for path in directory.glob('dotnet-diagnostic-*-socket'):
        match = re.fullmatch(r'dotnet-diagnostic-(\d+)-(\d+)-socket', path.name)
        if match:
            pid, ticks = map(int, match.groups())
            if pid in rows and rows[pid]['startTimeTicks'] == ticks:
                found[pid] = ticks
    return found


def command_json(argv, timeout=8):
    result = subprocess.run(argv, text=True, capture_output=True, timeout=timeout)
    if result.returncode:
        raise RuntimeError('Resource command failed: ' + argv[0])
    return json.loads(result.stdout)


class Ownership:
    def __init__(self, directories, output):
        self.directories = directories
        self.output = output
        self.identities = {}
        self.sessions = set()
        self.containers = {}
        self.positions = {}
        self.ready_containers = set()
        self.container_producers = {}
        self.errors = []
        self.last_docker_observation = 0.0

    def attach(self, pid):
        row = product.cost.numeric_process(pid)
        if row:
            self.identities[pid] = row['startTimeTicks']

    def observe(self, deadline=None):
        def execute(argv):
            remaining = 8 if deadline is None else min(8, deadline - time.monotonic())
            if remaining <= 0:
                raise TimeoutError('Owned cleanup observation deadline exceeded')
            return subprocess.run(argv, text=True, capture_output=True, timeout=remaining)
        rows = process_rows()
        self.identities.update(select_descendants(rows, self.identities))
        for directory in self.directories:
            paths = directory / 'environment-paths-original.json'
            if paths.exists():
                env = json.loads(paths.read_text())
                self.identities.update(diagnostic_identities(Path(env['TMPDIR']), rows))
            for path in (directory / 'phases').glob('*.jsonl'):
                with path.open() as stream:
                    stream.seek(self.positions.get(str(path), 0))
                    while True:
                        begin = stream.tell()
                        line = stream.readline()
                        if not line or not line.endswith('\n'):
                            stream.seek(begin)
                            break
                        event = json.loads(line)
                        emitter = int(event.get('processId', 0))
                        if emitter not in self.identities:
                            raise ValueError('Resource event emitted by unowned process')
                        if event.get('eventType') == 'resource' and event.get('kind') == 'process':
                            pid = int(event['id'])
                            row = rows.get(pid)
                            if row and row['startTimeTicks'] / os.sysconf('SC_CLK_TCK') <= event['ticks'] / event['frequency']:
                                self.identities[pid] = row['startTimeTicks']
                        if event.get('eventType') == 'resource' and event.get('kind') == 'container' and event.get('lifecycle') == 'ready':
                            cid = event.get('id', '')
                            if not re.fullmatch(r'[0-9a-f]{64}', cid):
                                raise ValueError('Invalid ready container ID')
                            self.ready_containers.add(cid)
                            self.container_producers[cid] = emitter
                        if event.get('eventType') == 'resource' and event.get('kind') == 'testcontainers-session':
                            session = event.get('id', '')
                            if not re.fullmatch(r'[0-9a-fA-F]{8}(?:-[0-9a-fA-F]{4}){3}-[0-9a-fA-F]{12}', session):
                                raise ValueError('Invalid session UUID')
                            self.sessions.add(session.lower())
                    self.positions[str(path)] = stream.tell()
        # A session is announced by its owned producer before Docker acquisition.
        inspect_docker = deadline is not None or time.monotonic() - self.last_docker_observation >= 1
        if inspect_docker:
            self.last_docker_observation = time.monotonic()
        for session in self.sessions if inspect_docker else ():
            labelled = execute(['docker', 'ps', '-aq', '--no-trunc', '--filter',
                'label=org.testcontainers.resource-reaper-session=' + session])
            reapers = execute(['docker', 'ps', '-aq', '--no-trunc', '--filter',
                'name=^/testcontainers-ryuk-' + session + '$'])
            if labelled.returncode or reapers.returncode:
                raise RuntimeError('Owned container enumeration failed')
            ids = labelled.stdout.split() + reapers.stdout.split()
            if ids:
                for cid in set(ids):
                    if cid in self.containers:
                        continue
                    inspected = execute(['docker', 'inspect', cid])
                    if inspected.returncode and 'no such object' in inspected.stderr.lower():
                        continue
                    if inspected.returncode:
                        raise RuntimeError('Owned container inspection failed')
                    data = json.loads(inspected.stdout)[0]
                    labels = data['Config'].get('Labels') or {}
                    labelled = labels.get('org.testcontainers.resource-reaper-session', '').lower() == session
                    reaper = (data['Name'] == '/testcontainers-ryuk-' + session
                              and data['Config']['Image'] == 'testcontainers/ryuk:0.14.0@sha256:7c1a8a9a47c780ed0f983770a662f80deb115d95cce3e2daa3d12115b8cd28f0')
                    if not labelled and not reaper:
                        raise ValueError('Container session ownership mismatch')
                    self.containers[data['Id']] = {'id': data['Id'], 'created': data['Created'],
                        'session': session, 'reaper': reaper, 'image': data['Config']['Image']}
        with self.output.open('a') as stream:
            stream.write(json.dumps({'monotonicSeconds': time.monotonic(),
                'identities': self.identities, 'sessions': sorted(self.sessions),
                'containers': list(self.containers.values()), 'readyContainers': sorted(self.ready_containers)}) + '\n')

    def alive(self):
        alive = {}
        for pid, ticks in self.identities.items():
            try:
                fields = Path(f'/proc/{pid}/stat').read_text().rsplit(') ', 1)[1].split()
                if int(fields[19]) == ticks and fields[0] != 'Z':
                    alive[pid] = ticks
            except (OSError, ValueError, IndexError):
                pass
        return alive

    def signal_identity(self, pid, ticks, sig):
        """pidfd binds the signal to the verified process, avoiding PID reuse races."""
        try:
            descriptor = os.pidfd_open(pid)
        except ProcessLookupError:
            return
        try:
            row = product.cost.numeric_process(pid)
            if row and row['startTimeTicks'] == ticks:
                signal.pidfd_send_signal(descriptor, sig)
        except ProcessLookupError:
            pass
        finally:
            os.close(descriptor)

    def terminate(self, sig):
        for pid, ticks in self.alive().items():
            self.signal_identity(pid, ticks, sig)

    def cleanup(self, grace=10, limit=30):
        started = time.monotonic()
        actions = []
        def bounded(argv):
            remaining = limit - (time.monotonic() - started)
            if remaining <= 0:
                raise TimeoutError('Owned cleanup deadline exceeded')
            return subprocess.run(argv, capture_output=True, text=True, timeout=min(8, remaining))
        self.observe(deadline=started + limit)
        while self.alive() and time.monotonic() - started < grace:
            time.sleep(.2)
            self.observe(deadline=started + limit)
        if self.alive():
            actions.append({'signal': 'SIGTERM', 'identities': self.alive()})
            self.terminate(signal.SIGTERM)
        while self.alive() and time.monotonic() - started < limit - 5:
            time.sleep(.2)
            self.observe(deadline=started + limit)
        if self.alive():
            actions.append({'signal': 'SIGKILL', 'identities': self.alive()})
            self.terminate(signal.SIGKILL)
        remaining_containers = []
        # Every removal rechecks immutable creation identity and original ownership.
        for cid, original in sorted(self.containers.items(), key=lambda item: item[1]['reaper']):
            check = bounded(['docker', 'inspect', cid])
            if check.returncode:
                if 'no such object' in check.stderr.lower():
                    continue
                raise RuntimeError('Cleanup inspection unavailable')
            data = json.loads(check.stdout)[0]
            labels = data['Config'].get('Labels') or {}
            owned = (labels.get('org.testcontainers.resource-reaper-session', '').lower() == original['session']
                     or (original['reaper'] and data['Name'] == '/testcontainers-ryuk-' + original['session']
                         and data['Config']['Image'] == original['image']))
            if data['Created'] != original['created'] or not owned:
                raise ValueError('Container identity changed before cleanup')
            result = bounded(['docker', 'rm', '-f', cid])
            actions.append({'container': cid, 'exitCode': result.returncode})
            if result.returncode and not ('no such object' in result.stderr.lower() or 'no such container' in result.stderr.lower()):
                remaining_containers.append(cid)
        # Reaper deletion may leave session-labelled volumes/networks on cancellation.
        # Network IDs are immutable. Docker volumes expose no immutable ID, so a
        # residual volume fails cleanup rather than risking deletion by reused name.
        resource_bindings = []
        remaining_resources = []
        for session in self.sessions:
            for kind in ('volume', 'network'):
                listing = bounded(['docker', kind, 'ls', '-q', '--filter',
                    'label=org.testcontainers.resource-reaper-session=' + session])
                if listing.returncode:
                    raise RuntimeError('Owned resource enumeration unavailable')
                for resource in listing.stdout.split():
                    inspected = bounded(['docker', kind, 'inspect', resource])
                    if inspected.returncode:
                        if 'no such' in inspected.stderr.lower() or 'not found' in inspected.stderr.lower():
                            continue
                        raise RuntimeError('Owned resource inspection unavailable')
                    data = json.loads(inspected.stdout)[0]
                    if (data.get('Labels') or {}).get('org.testcontainers.resource-reaper-session', '').lower() != session:
                        raise ValueError('Resource ownership changed before cleanup')
                    identity = {'kind': kind, 'session': session, 'name': data['Name'],
                                'created': data.get('CreatedAt' if kind == 'volume' else 'Created'),
                                'id': data.get('Id')}
                    resource_bindings.append(identity)
                    if kind == 'volume':
                        remaining_resources.append(resource)
                        continue
                    if not identity['id'] or not identity['created']:
                        raise ValueError('Network lacks immutable creation identity')
                    verified = bounded(['docker', 'network', 'inspect', identity['id']])
                    if verified.returncode:
                        if 'no such' in verified.stderr.lower() or 'not found' in verified.stderr.lower():
                            continue
                        raise RuntimeError('Owned network reinspection unavailable')
                    current = json.loads(verified.stdout)[0]
                    if (current['Id'] != identity['id'] or current['Created'] != identity['created']
                        or current['Name'] != identity['name']
                        or (current.get('Labels') or {}).get('org.testcontainers.resource-reaper-session', '').lower() != session):
                        raise ValueError('Network creation identity changed before cleanup')
                    result = bounded(['docker', 'network', 'rm', identity['id']])
                    actions.append({'network': identity['id'], 'exitCode': result.returncode, 'identity': identity})
                    if result.returncode and not ('no such' in result.stderr.lower() or 'not found' in result.stderr.lower()):
                        remaining_resources.append(identity['id'])
        # Re-enumerate after every owned process has stopped: Docker may finish a
        # creation request after its client exited. Snapshot twice; never substitute
        # removal exit codes for a fresh session-scoped absence observation.
        final_observations = []
        for observation in range(2):
            self.observe(deadline=started + limit)
            residual = []
            for session in self.sessions:
                queries = [('container', ['docker', 'ps', '-aq', '--no-trunc', '--filter',
                            'label=org.testcontainers.resource-reaper-session=' + session]),
                           ('ryuk', ['docker', 'ps', '-aq', '--no-trunc', '--filter',
                            'name=^/testcontainers-ryuk-' + session + '$']),
                           ('volume', ['docker', 'volume', 'ls', '-q', '--filter',
                            'label=org.testcontainers.resource-reaper-session=' + session]),
                           ('network', ['docker', 'network', 'ls', '-q', '--filter',
                            'label=org.testcontainers.resource-reaper-session=' + session])]
                for kind, argv in queries:
                    result = bounded(argv)
                    if result.returncode:
                        raise RuntimeError('Final owned resource enumeration unavailable')
                    residual.extend({'kind': kind, 'id': value, 'session': session} for value in result.stdout.split())
            final_observations.append({'monotonicSeconds': time.monotonic(), 'resources': residual,
                                       'processes': self.alive()})
            if observation == 0:
                time.sleep(.25)
        remaining_containers = sorted(set(remaining_containers) | {r['id'] for sample in final_observations
                                      for r in sample['resources'] if r['kind'] in ('container', 'ryuk')})
        remaining_resources = sorted(set(remaining_resources) | {r['id'] for sample in final_observations
                                     for r in sample['resources'] if r['kind'] in ('volume', 'network')})
        return {'seconds': time.monotonic() - started, 'actions': actions,
                'resourceBindings': resource_bindings, 'finalObservations': final_observations,
                'remainingProcesses': self.alive(), 'remainingContainers': remaining_containers,
                'remainingResources': remaining_resources,
                'complete': not (self.alive() or remaining_containers or remaining_resources)}



def environment(directory):
    env = os.environ.copy()
    temporary = tempfile.mkdtemp(prefix='ax405-')
    for key in ('TMPDIR', 'TESTINGPLATFORM_PIPE_DIRECTORY'):
        env[key] = temporary
    for key, leaf in [('DOTNET_CLI_HOME', 'cli'), ('NUGET_PACKAGES', 'nuget'),
                      ('MSBuildUserExtensionsPath', 'msbuild'), ('DRIVER_PRIVACY_EVIDENCE_DIR', 'evidence'),
                      ('APEXRACERS_COST_PROBE_DIR', 'phases'), ('PUBLICATION_REHEARSAL_ARTIFACTS', 'publication')]:
        target = directory / leaf
        target.mkdir()
        env[key] = str(target)
    env['DOTNET_CLI_USE_MSBUILD_SERVER'] = '0'
    env['MSBUILDDISABLENODEREUSE'] = '1'
    write(directory / 'environment-paths-original.json', {key: env[key] for key in
          ('TMPDIR', 'TESTINGPLATFORM_PIPE_DIRECTORY', 'DOTNET_CLI_HOME', 'NUGET_PACKAGES',
           'MSBuildUserExtensionsPath', 'DRIVER_PRIVACY_EVIDENCE_DIR', 'APEXRACERS_COST_PROBE_DIR',
           'PUBLICATION_REHEARSAL_ARTIFACTS', 'DOTNET_CLI_USE_MSBUILD_SERVER', 'MSBUILDDISABLENODEREUSE')})
    return env


def children(config):
    items = json.loads(Path(config).read_text())
    processes = []
    interrupted = False
    def stop(*_):
        nonlocal interrupted
        interrupted = True
    signal.signal(signal.SIGTERM, stop)
    signal.signal(signal.SIGINT, stop)
    for item in items['producers']:
        directory = Path(item['directory'])
        command = product.invocation(Path(item['executable']), directory, item['uids'])
        write(directory / 'command-original.json', command)
        log = (directory / 'original.log').open('w')
        proc = subprocess.Popen(command, cwd=ROOT, env=environment(directory), stdout=log, stderr=subprocess.STDOUT)
        processes.append((proc, directory, log))
    write(Path(config).parent / 'producer-launch-original.json', [{'pid': p.pid, 'directory': str(d)} for p, d, _ in processes])
    while any(p.poll() is None for p, _, _ in processes):
        if interrupted or any(p.poll() not in (None, 0) for p, _, _ in processes):
            for p, _, _ in processes:
                if p.poll() is None:
                    p.terminate()
        time.sleep(.1)
    codes = []
    for proc, directory, log in processes:
        code = proc.wait()
        log.close()
        write(directory / 'original-exit.json', {'exitCode': code})
        codes.append(code)
    return 0 if codes == [0, 0] and not interrupted else 1


def verify_partition(rows, shards):
    if len(shards) != 2 or any(not s for s in shards):
        raise ValueError('Exactly two nonempty producers required')
    expected = {r['uid']: r for r in rows}
    if len(expected) != len(rows) or Counter(r['uid'] for s in shards for r in s) != Counter(r['uid'] for r in rows):
        raise ValueError('Partition incomplete, overlapping or ambiguous')
    owners = {}
    for index, shard in enumerate(shards):
        for row in shard:
            if row != expected[row['uid']]:
                raise ValueError('Changed partition row')
            cls = (row['type']['namespace'], row['type']['typeName'])
            if cls in owners and owners[cls] != index:
                raise ValueError('Class split across producers')
            owners[cls] = index


def validate_original_results(directory, cases):
    expected = Counter(r['displayName'] for r in cases)
    if not cases or any(count != 1 for count in expected.values()):
        raise ValueError('Empty or ambiguous expected executions')
    original_exit = json.loads((directory / 'original-exit.json').read_text())
    if original_exit.get('exitCode') != 0:
        raise ValueError('Original producer exited nonzero')
    if product.cost.read_results(directory / 'original.trx') != (expected, Counter({'Passed': len(cases)})):
        raise ValueError('Missing/duplicate/skipped/nonpassing original executions')


def requires_postgres(cases):
    for case in cases:
        path = Path(case['location']['file'])
        source = path.read_text()
        if 'PostgreSqlFixture' in source or 'PostgreSqlCollection.Name' in source:
            return True
    return False


def validate_probe_evidence(directory, cases, complete=True):
    log = directory / 'original.log'
    if log.exists() and 'FIXTURE_COST_PROBE_INVALID' in log.read_text():
        raise ValueError('Invalid fixture instrumentation; ownership evidence incomplete')
    if not requires_postgres(cases):
        return
    events = []
    for path in (directory / 'phases').glob('*.jsonl'):
        for line in path.read_text().splitlines():
            events.append(json.loads(line))
    sessions = [e for e in events if e.get('kind') == 'testcontainers-session']
    ready = [e for e in events if e.get('kind') == 'container' and e.get('lifecycle') == 'ready']
    disposed = {e['id'] for e in events if e.get('kind') == 'container' and e.get('lifecycle') == 'disposed'}
    if len(sessions) != 1 or len(ready) != 1 or sessions[0]['processId'] != ready[0]['processId']:
        raise ValueError('Expected PG producer session/ready ownership evidence missing or ambiguous')
    if complete and ready[0]['id'] not in disposed:
        raise ValueError('Successful PG producer omitted disposal evidence')


def snapshots(tool):
    return {'sourceBuild': product.cost.inventory(), 'sdk': product.cost.sdk_inventory(),
            'tool': product.probe.inventory(tool.parent / '.store/dotnet-coverage/18.12.0'),
            'runtime': product.probe.inventory(Path(shutil.which('dotnet')).resolve().parent / 'shared'),
            'runnerInputs': {str(p.relative_to(ROOT)): product.cost.digest(p) for p in
                (Path(__file__), ROOT / 'scripts/probe-product-coverage-preservation.py',
                 ROOT / 'scripts/probe-shared-product-coverage.py', ROOT / 'scripts/probe-coverage-preservation.py',
                 ROOT / 'scripts/measure-backend-cost.py', ROOT / 'docs/testing/backend-fixture-cost.json')}}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--no-build', action='store_true', help='Accepted explicitly; this runner always consumes an existing canonical Release build')
    parser.add_argument('--mode', choices=('baseline', 'prototype'), required=True)
    parser.add_argument('--tool', type=Path, required=True)
    parser.add_argument('--output', type=Path, required=True)
    parser.add_argument('--reference', type=Path)
    parser.add_argument('--fault', choices=('producer-exit', 'cancel'))
    parser.add_argument('--fault-after', type=float, default=15)
    parser.add_argument('--filter-class', action='append')
    args = parser.parse_args()
    out = args.output.resolve()
    out.mkdir(parents=True, exist_ok=False)
    tool = args.tool.resolve()
    context = {'schemaVersion': 1, 'mode': args.mode, 'commands': [], 'fullSuite': not args.filter_class,
               'sourceHead': subprocess.check_output(['git', 'rev-parse', 'HEAD'], cwd=ROOT, text=True).strip(),
               'liveEligible': False, 'adoptionEligible': False, 'accepted': False}
    write(out / 'context.json', context)
    shutil.copy2(__file__, out / 'runner-original.py')
    interrupted = False
    def stop(*_):
        nonlocal interrupted
        interrupted = True
    signal.signal(signal.SIGTERM, stop)
    signal.signal(signal.SIGINT, stop)
    ownership = Ownership([], out / 'ownership-original.jsonl')
    context['producerDirectories'] = []
    def run(label, command, env=None, fault=None):
        directory = out / label
        directory.mkdir(exist_ok=True)
        write(directory / 'command-original.json', list(map(str, command)))
        start = time.monotonic()
        with (directory / 'original.log').open('w') as log:
            proc = subprocess.Popen(list(map(str, command)), cwd=ROOT, env=env, stdout=log,
                                    stderr=subprocess.STDOUT, start_new_session=True)
            ownership.attach(proc.pid)
            injected = False
            ready_at = None
            cancelled_at = None
            while proc.poll() is None:
                ownership.observe()
                if label in ('baseline', 'collection') and fault and ready_at is None and any(c['image'].startswith('postgres:') and c['id'] in ownership.ready_containers for c in ownership.containers.values()):
                    ready_at = time.monotonic()
                if (fault == 'producer-exit' and not injected and ready_at is not None
                    and time.monotonic() - ready_at >= args.fault_after):
                    launch = json.loads((out / 'producer-launch-original.json').read_text())
                    ready_pids = {ownership.container_producers[cid] for cid in ownership.ready_containers}
                    victim = next((r['pid'] for r in launch if r['pid'] in ready_pids), None)
                    if victim is None:
                        raise ValueError('Ready PostgreSQL container has no directly owned producer')
                    if victim not in ownership.alive():
                        raise ValueError('Fault target no longer owned/alive')
                    ownership.signal_identity(victim, ownership.identities[victim], signal.SIGTERM)
                    injected = True
                    context['faultInjected'] = True
                    cancelled_at = time.monotonic()
                    write(out / 'injected-fault-original.json', {'kind': fault, 'pid': victim, 'afterContainerObserved': True})
                if fault and ready_at is None and time.monotonic() - start > 180:
                    context['faultStartupTimeout'] = True
                    ownership.terminate(signal.SIGTERM)
                    cancelled_at = cancelled_at or time.monotonic()
                if interrupted or (fault == 'cancel' and ready_at is not None and time.monotonic() - ready_at >= args.fault_after):
                    if not injected:
                        injected = True
                        context['cancelRequested'] = True
                        context['faultInjected'] = fault == 'cancel'
                        cancelled_at = time.monotonic()
                        ownership.terminate(signal.SIGTERM)
                if cancelled_at is not None and time.monotonic() - cancelled_at > 5:
                    ownership.terminate(signal.SIGKILL)
                time.sleep(.2)
            code = proc.wait()
        write(directory / 'original-exit.json', {'exitCode': code, 'interrupted': injected})
        context['commands'].append({'label': label, 'argv': list(map(str, command)), 'exitCode': code,
                                    'seconds': time.monotonic() - start, 'interrupted': injected})
        write(out / 'context.json', context)
        if code or injected:
            raise ValueError('Original execution failed/cancelled: ' + label)
        return directory
    try:
        version = subprocess.check_output([str(tool), '--version'], text=True).strip()
        if os.environ.get('TESTCONTAINERS_RYUK_DISABLED', '').lower() in ('true', '1') or os.environ.get('TESTCONTAINERS_REUSE_ENABLE', '').lower() in ('true', '1'):
            raise ValueError('Ryuk disabled or resource reuse enabled')
        if not version.startswith('18.12.0+'):
            raise ValueError('Collector must be pinned18.12.0')
        context['toolVersion'] = version
        context['before'] = snapshots(tool)
        discovery = run('discovery', [product.cost.TEST_OUTPUT / 'ApexRacers.Tests', '--list-tests', 'json'])
        allrows = json.loads((discovery / 'original.log').read_text())['tests']
        shared.map_discovery(allrows, allrows)
        rows = allrows
        if args.filter_class:
            rows = [r for r in rows if r['type']['namespace'] + '.' + r['type']['typeName'] in args.filter_class]
            if {r['type']['namespace'] + '.' + r['type']['typeName'] for r in rows} != set(args.filter_class):
                raise ValueError('Unknown or empty smoke class')
        if not rows:
            raise ValueError('Zero discovered executions')
        write(out / 'discovery-original.json', {'tests': rows})
        context['discoverySha256'] = product.cost.digest(out / 'discovery-original.json')
        if args.mode == 'baseline':
            directory = out / 'baseline'
            directory.mkdir()
            ownership.directories.append(directory)
            context['producerDirectories'] = [str(p) for p in ownership.directories]
            write(out / 'context.json', context)
            cmd = product.invocation(product.cost.TEST_OUTPUT / 'ApexRacers.Tests', directory,
                                     [r['uid'] for r in rows] if args.filter_class else None)
            cmd += ['--coverage', '--coverage-output', 'original.xml', '--coverage-output-format', 'cobertura',
                    '--coverage-settings', str(ROOT / 'coverage.runsettings')]
            reports = [(directory, rows)]
            run('baseline', cmd, environment(directory), args.fault)
            coverage = directory / 'original.xml'
        else:
            if not args.reference:
                raise ValueError('Prototype requires original baseline reference')
            reference = args.reference.resolve()
            control = json.loads(reference.read_text())
            originalrows = json.loads((reference.parent / 'discovery-original.json').read_text())['tests']
            if (not control.get('accepted') or control.get('mode') != 'baseline'
                or control['before'] != context['before'] or originalrows != rows):
                raise ValueError('Baseline missing, rejected, stale or materially changed')
            originalpaths = [reference, reference.parent / 'discovery-original.json',
                             reference.parent / 'baseline/original.xml', reference.parent / 'baseline/original.trx']
            for p in originalpaths[1:]:
                key = str(p.relative_to(reference.parent))
                if control.get('originalArtifacts', {}).get(key) != product.cost.digest(p):
                    raise ValueError('Original baseline artifact changed: ' + key)
            if control.get('after') != control['before'] or product.cost.read_results(reference.parent / 'baseline/original.trx') != (Counter(r['displayName'] for r in originalrows), Counter({'Passed': len(originalrows)})):
                raise ValueError('Original baseline outcomes or material bindings invalid')
            reference_hashes = {str(p): product.cost.digest(p) for p in originalpaths}
            context['referenceFilesBefore'] = reference_hashes
            shards, weights = product.plan_shards(rows)
            verify_partition(rows, shards)
            write(out / 'partition-original.json', {'shards': shards, 'weights': weights})
            settings = ET.parse(ROOT / 'coverage.runsettings').getroot()
            for key, value in [('IncludeTestAssembly', 'False'), ('ExcludeAssembliesWithoutSources', 'MissingAll')]:
                ET.SubElement(settings, key).text = value
            for key, value in [('UseVerifiableInstrumentation', 'False'), ('EnableStaticManagedInstrumentation', 'False'),
                ('EnableDynamicManagedInstrumentation', 'True'), ('EnableStaticNativeInstrumentation', 'False'),
                ('EnableDynamicNativeInstrumentation', 'False'), ('CollectFromChildProcesses', 'True')]:
                ET.SubElement(settings.find('CodeCoverage'), key).text = value
            ET.ElementTree(settings).write(out / 'settings-original.config', encoding='utf-8', xml_declaration=True)
            producers, reports = [], []
            for index, shard in enumerate(shards, 1):
                directory = out / f'producer-{index}'
                directory.mkdir()
                ownership.directories.append(directory)
                context['producerDirectories'] = [str(p) for p in ownership.directories]
                write(out / 'context.json', context)
                copied = directory / 'build'
                shutil.copytree(product.cost.TEST_OUTPUT, copied)
                if product.probe.inventory(copied) != product.probe.inventory(product.cost.TEST_OUTPUT):
                    raise ValueError('Copied product build differs')
                discovered = run(f'discovery-{index}', [copied / 'ApexRacers.Tests', '--list-tests', 'json'])
                mapping = shared.map_discovery(allrows, json.loads((discovered / 'original.log').read_text())['tests'])
                write(directory / 'mapping-original.json', [{'canonicalUid': r['uid'], 'copiedUid': mapping[r['displayName']],
                                                            'displayName': r['displayName']} for r in shard])
                producers.append({'directory': str(directory), 'executable': str(copied / 'ApexRacers.Tests'),
                                  'uids': [mapping[r['displayName']] for r in shard]})
                reports.append((directory, shard))
            write(out / 'children-original.json', {'producers': producers, 'fault': args.fault, 'faultAfter': args.fault_after})
            cmd = [tool, 'collect', '--settings', out / 'settings-original.config', '--output-format', 'cobertura',
                   '--output', out / 'original.xml', 'python3', Path(__file__).resolve(), '--children', out / 'children-original.json']
            run('collection', cmd, fault=args.fault)
            coverage = out / 'original.xml'
            baseline = reference.parent / 'baseline/original.xml'
            if (product.normalized_report(baseline) != product.normalized_report(coverage)
                or product.normalized_report(baseline, True) != product.normalized_report(coverage, True)
                or any(ET.parse(baseline).getroot().get(k) != ET.parse(coverage).getroot().get(k)
                       for k in ('lines-valid', 'lines-covered', 'branches-valid', 'branches-covered'))):
                raise ValueError('Coverage model, observations or root counts differ')
            if {str(p): product.cost.digest(p) for p in originalpaths} != reference_hashes:
                raise ValueError('Original reference changed')
            context['coverageEqual'] = True
        for directory, cases in reports:
            validate_probe_evidence(directory, cases)
            validate_original_results(directory, cases)
        if context['fullSuite']:
            context['coverage'] = product.coverage_gate(coverage)
        else:
            context['coverage'] = ET.parse(coverage).getroot().attrib
        context['executionCount'] = len(rows)
        context['after'] = snapshots(tool)
        if context['after'] != context['before']:
            raise ValueError('Material inputs changed')
        context['executionValidated'] = True
    except BaseException as error:
        context['error'] = {'type': type(error).__name__, 'message': str(error)}
    finally:
        signal.signal(signal.SIGTERM, signal.SIG_IGN)
        signal.signal(signal.SIGINT, signal.SIG_IGN)
        try:
            context['cleanup'] = ownership.cleanup(grace=3 if args.fault or interrupted else 10)
        except BaseException as error:
            context['cleanup'] = {'complete': False, 'errorType': type(error).__name__, 'message': str(error)}
        context['accepted'] = bool(context.get('executionValidated') and context['cleanup'].get('complete'))
        context['faultRehearsalPassed'] = bool(args.fault and context.get('faultInjected') and not context['accepted'] and context.get('error')
                                              and context['cleanup'].get('complete'))
        if any('FIXTURE_COST_PROBE_INVALID' in p.read_text() for p in out.glob('*/original.log')):
            context['cleanup']['complete'] = False
            context['cleanup']['evidenceError'] = 'FIXTURE_COST_PROBE_INVALID'
        if 'reports' in locals():
            for directory, cases in reports:
                try:
                    validate_probe_evidence(directory, cases, complete=bool(context.get('executionValidated')))
                except BaseException as error:
                    context['cleanup']['complete'] = False
                    context['cleanup']['evidenceError'] = str(error)
        context['accepted'] = bool(context.get('executionValidated') and context['cleanup'].get('complete'))
        context['faultRehearsalPassed'] = bool(args.fault and context.get('faultInjected') and not context['accepted'] and context.get('error')
                                              and context['cleanup'].get('complete'))
        context['originalArtifacts'] = {str(p.relative_to(out)): product.cost.digest(p) for p in out.rglob('*')
                                        if p.is_file() and p.name != 'context.json' and not any(part in ('build', 'nuget', 'cli', 'msbuild') for part in p.relative_to(out).parts)}
        write(out / 'context.json', context)
    print(out / 'context.json')
    return 0 if context['accepted'] else 1


if __name__ == '__main__':
    if len(sys.argv) == 3 and sys.argv[1] == '--cleanup-context':
        reference = Path(sys.argv[2]).resolve()
        context = json.loads(reference.read_text())
        owner = Ownership([Path(p) for p in context.get('producerDirectories', [])], reference.parent / 'cleanup-followup-observations.jsonl')
        original = reference.parent / 'ownership-original.jsonl'
        if original.exists():
            for line in original.read_text().splitlines():
                data = json.loads(line)
                owner.identities.update({int(k): v for k, v in data['identities'].items()})
                owner.sessions.update(data['sessions'])
                owner.containers.update({c['id']: c for c in data['containers']})
        result = owner.cleanup(grace=0)
        write(reference.parent / 'cleanup-followup.json', result)
        raise SystemExit(0 if result['complete'] else 1)
    if len(sys.argv) == 3 and sys.argv[1] == '--children':
        raise SystemExit(children(sys.argv[2]))
    raise SystemExit(main())
