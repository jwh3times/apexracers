#!/usr/bin/env python3
"""Isolated #405 cost prototype: preserve originals, reject incomplete evidence, never retry."""
import argparse
from collections import Counter, defaultdict
from datetime import datetime, timezone
import hashlib
import json
import os
import re
from pathlib import Path
import signal
import shutil
import subprocess
import time
import xml.etree.ElementTree as ET

NS = {'t': 'http://microsoft.com/schemas/VisualStudio/TeamTest/2010'}
TEST_OUTPUT = Path('src/ApexRacers.Tests/bin/Release/net10.0')
LEGACY = 'abf8050f3d12b0b667c3501d3246416a0c9feca8'

def sha(path): return hashlib.sha256(Path(path).read_bytes()).hexdigest()
def canonical(value): return hashlib.sha256(json.dumps(value, sort_keys=True, separators=(',', ':')).encode()).hexdigest()
def load(path): return json.loads(Path(path).read_text())
def write(path, value): Path(path).write_text(json.dumps(value, indent=2, sort_keys=True) + '\n')
def seconds(value):
    h,m,s = value.split(':'); return int(h)*3600+int(m)*60+float(s)
def results(path):
    root = ET.parse(path).getroot()
    rows = root.findall('t:Results/t:UnitTestResult', NS)
    if not rows: raise ValueError('No original TRX cases')
    for key in ('testId', 'executionId'):
        identities = [row.get(key) for row in rows]
        if None in identities or len(set(identities)) != len(identities): raise ValueError('Missing/duplicate original TRX ' + key)
    return [{'name': r.attrib['testName'], 'outcome': r.attrib['outcome'], 'seconds': seconds(r.attrib['duration'])} for r in rows]
def inventory(root):
    root = Path(root)
    files = [p for p in (root/'src').rglob('*') if p.is_file() and not {'bin','obj'}.intersection(p.relative_to(root).parts)]
    files += [p for p in (root/TEST_OUTPUT).rglob('*') if p.is_file()]
    files += [root/p for p in ('ApexRacers.sln','global.json','Directory.Build.props','Directory.Build.targets','Directory.Packages.props','coverage.runsettings','docs/testing/driver-catalog-admission-v1.json') if (root/p).is_file()]
    return {str(p.relative_to(root)): sha(p) for p in sorted(files)}
def discovery_rows(doc):
    rows = doc['tests']
    if not rows or len({r['uid'] for r in rows}) != len(rows): raise ValueError('Empty or duplicate discovery UIDs')
    for r in rows:
        if not r['uid'] or not r['displayName']: raise ValueError('Incomplete discovery identity')
    if len({row['displayName'] for row in rows}) != len(rows):
        raise ValueError('Ambiguous display names cannot join original TRX executions to discovery UIDs')
    return rows

def sdk_fingerprint():
    version=subprocess.check_output(['dotnet','--version'],text=True).strip()
    paths=subprocess.check_output(['dotnet','--list-sdks'],text=True).splitlines()
    matching=[line for line in paths if line.startswith(version+' [')]
    if len(matching)!=1: raise ValueError('Ambiguous SDK installation')
    directory=Path(matching[0].split(' [',1)[1].removesuffix(']'))/version
    manifest={str(p.relative_to(directory)):sha(p) for p in sorted(directory.rglob('*')) if p.is_file()}
    manifest['$dotnet-host']=sha(Path(shutil.which('dotnet')).resolve())
    if not manifest: raise ValueError('Empty SDK inventory')
    return canonical(manifest)

def plan(args):
    doc = load(args.discovery); rows = discovery_rows(doc)
    weight_document = load(args.weights)
    coverage_runs = [r for r in weight_document['runs'] if r['result']['coverage']]
    class_samples = defaultdict(list)
    for run in coverage_runs:
        totals = defaultdict(float)
        for method, observation in run['methods'].items():
            totals[method.rsplit('.', 1)[0]] += observation['trxSeconds']
        for name, value in totals.items(): class_samples[name].append(value)
    import statistics
    classes = defaultdict(list)
    for row in rows:
        classes[class_name(row)].append(row)
    weights = {name: statistics.median(class_samples[name]) if name in class_samples else len(cases) * .05
               for name, cases in classes.items()}
    bins = [[], []]; totals = [0., 0.]
    for name in sorted(classes, key=lambda name: (-weights[name], name)):
        index = min(range(2), key=lambda i: (totals[i], i))
        bins[index].extend(classes[name]); totals[index] += weights[name]
    manifest = inventory(args.root)
    result = {'schemaVersion': 1, 'scope': 'Cost prototype only; no privacy/live acceptance or production adoption',
              'runnerSha256': sha(Path(__file__)), 'compileRoot': str(args.root.resolve()), 'runId': args.run_id, 'sourceHead': subprocess.check_output(['git', '-C', str(args.root), 'rev-parse', 'HEAD'], text=True).strip(),
              'legacyCommit': LEGACY, 'sdk': subprocess.check_output(['dotnet', '--version'], text=True).strip(),
              'sdkFingerprint': sdk_fingerprint(), 'buildInventory': manifest, 'buildSha256': canonical(manifest),
              'discovery': doc, 'discoverySha256': canonical(doc), 'weightsSha256': sha(args.weights),
              'estimatedCaseSeconds': totals, 'estimatedClasses': sorted(set(classes) - set(class_samples)), 'shards': bins}
    result['planSha256'] = canonical(result)
    verify_plan(result)
    if args.output.exists(): raise ValueError('Refusing to replace an original plan')
    write(args.output, result)


def class_name(row):
    return row['type']['namespace'] + '.' + row['type']['typeName']

def verify_plan(p):
    bound=dict(p); expected=bound.pop('planSha256');
    if canonical(bound)!=expected: raise ValueError('Plan binding changed')
    rows=discovery_rows(p['discovery']); flat=[r for shard in p['shards'] for r in shard]
    if len(p['shards'])!=2 or Counter(canonical(r) for r in flat)!=Counter(canonical(r) for r in rows): raise ValueError('Missing/duplicate shard UID')
    owner={}
    for i,rs in enumerate(p['shards']):
        if not rs: raise ValueError('Empty shard')
        for r in rs:
            c=class_name(r)
            if owner.setdefault(c,i)!=i: raise ValueError('Class split across shards')
    if canonical(p['buildInventory'])!=p['buildSha256']: raise ValueError('Build binding changed')
    if canonical(p['discovery'])!=p['discoverySha256']: raise ValueError('Discovery binding changed')

def docker_ids():
    return set(subprocess.check_output(['docker', 'ps', '-aq', '--no-trunc'], text=True).split())


def group_members(group):
    members = []
    for path in Path('/proc').glob('[0-9]*/stat'):
        try:
            fields = path.read_text().rsplit(')', 1)[1].split()
            if int(fields[2]) == group and fields[0] != 'Z':
                members.append({'pid': int(path.parent.name), 'startTicks': int(fields[19])})
        except (FileNotFoundError, ProcessLookupError): pass
    return members


def execute(args):
    p = load(args.plan); verify_plan(p); root = args.root.resolve()
    expected = p['discovery']['tests'] if args.producer == 'baseline' else p['shards'][int(args.producer[-1]) - 1]
    args.output.mkdir(parents=True, exist_ok=False); out = args.output.resolve()
    status = {'schemaVersion': 1, 'runId': p['runId'], 'producer': args.producer,
              'planSha256': p['planSha256'], 'buildSha256': p['buildSha256'], 'sourceHead': p['sourceHead'],
              'sdk': p['sdk'], 'sdkFingerprint': p['sdkFingerprint'], 'status': 'failed',
              'exitCode': None, 'inventoryBefore': False, 'inventoryAfter': False,
              'cancelled': False, 'cleanupVerified': False, 'filteredDiscoveryMatches': False,
              'startedAt': datetime.now(timezone.utc).isoformat()}
    child = None; cancelled_at = None; start = time.monotonic()
    def cancel(signum, _):
        nonlocal cancelled_at
        status['cancelled'] = True
        cancelled_at = cancelled_at or time.monotonic()
        if child and child.poll() is None:
            try: os.killpg(child.pid, signal.SIGTERM)
            except ProcessLookupError: pass
    handlers = {sig: signal.signal(sig, cancel) for sig in (signal.SIGTERM, signal.SIGINT)}
    try:
        if sha(Path(__file__)) != p['runnerSha256']: raise ValueError('Prototype runner bytes differ from planned runner')
        if str(root) != p['compileRoot']: raise ValueError('Compiled CallerFilePath root differs from execution checkout')
        if subprocess.check_output(['git', '-C', str(root), 'rev-parse', 'HEAD'], text=True).strip() != p['sourceHead']: raise ValueError('Checkout commit differs')
        subprocess.run(['git', '-C', str(root), 'cat-file', '-e', p['legacyCommit'] + '^{commit}'], check=True, capture_output=True)
        status['inventoryBefore'] = inventory(root) == p['buildInventory']
        if not status['inventoryBefore']: raise ValueError('Source/build snapshot changed')
        if subprocess.check_output(['dotnet', '--version'], text=True).strip() != p['sdk'] or sdk_fingerprint() != p['sdkFingerprint']:
            raise ValueError('SDK version/bytes differ')
        env = os.environ.copy()
        directories = {'DRIVER_PRIVACY_EVIDENCE_DIR': 'evidence', 'PUBLICATION_REHEARSAL_ARTIFACTS': 'publication',
                       'APEXRACERS_COST_PROBE_DIR': 'phases', 'TMPDIR': 'tmp', 'NUGET_PACKAGES': 'nuget',
                       'DOTNET_CLI_HOME': 'cli-home', 'MSBuildUserExtensionsPath': 'msbuild-user'}
        for key, name in directories.items():
            env[key] = str(out / name); Path(env[key]).mkdir()
        env['DOTNET_SKIP_FIRST_TIME_EXPERIENCE'] = '1'
        binary = root / TEST_OUTPUT / 'ApexRacers.Tests'
        selection = [] if args.producer == 'baseline' else ['--filter-uid', *[r['uid'] for r in expected]]
        discovery = subprocess.run([str(binary), *selection, '--list-tests', 'json'], cwd=root, env=env,
                                   capture_output=True, text=True, check=True)
        (out / 'filtered-discovery-original.json').write_text(discovery.stdout)
        actual = discovery_rows(json.loads(discovery.stdout))
        status['filteredDiscoveryMatches'] = Counter(canonical(r) for r in actual) == Counter(canonical(r) for r in expected)
        if not status['filteredDiscoveryMatches']: raise ValueError('Compiled filtered discovery differs')
        before = docker_ids(); write(out / 'docker-before.json', sorted(before))
        version = subprocess.check_output([str(args.tool), '--version'], text=True).strip()
        if version.split('+')[0] != '18.12.0': raise ValueError('Unpinned coverage export tool')
        status['coverageTool'] = version
        command = [str(binary), *selection, '--coverage', '--coverage-output', 'original.coverage',
                   '--coverage-output-format', 'coverage', '--coverage-settings', str(root / 'coverage.runsettings'),
                   '--report-xunit-trx', '--report-xunit-trx-filename', 'original.trx', '--results-directory', str(out)]
        write(out / 'command.json', command)
        if status['cancelled']: raise ValueError('Cancelled before test invocation')
        command_start = time.monotonic()
        with (out / 'original.log').open('w') as log:
            child = subprocess.Popen(command, cwd=root, env=env, stdout=log, stderr=subprocess.STDOUT, start_new_session=True)
            while child.poll() is None:
                if cancelled_at is not None and time.monotonic() - cancelled_at >= 10:
                    try: os.killpg(child.pid, signal.SIGKILL)
                    except ProcessLookupError: pass
                time.sleep(.1)
            status['exitCode'] = child.wait()
        status['commandSeconds'] = time.monotonic() - command_start
        if (out / 'original.coverage').is_file():
            with (out / 'coverage-export-original.log').open('w') as log:
                subprocess.run([str(args.tool), 'merge', str(out / 'original.coverage'), '-f', 'cobertura',
                                '-o', str(out / 'coverage.cobertura.xml')], stdout=log, stderr=subprocess.STDOUT, check=True)
        # Observe actual cleanup, preserving leftovers as evidence. Never remove an unrelated container.
        deadline = time.monotonic() + 15
        while True:
            members = group_members(child.pid); leftovers = docker_ids() - before
            if not members and not leftovers or time.monotonic() >= deadline: break
            time.sleep(.5)
        write(out / 'cleanup.json', {'group': child.pid, 'remainingProcesses': members,
                                    'additionalContainers': sorted(leftovers), 'observedAt': datetime.now(timezone.utc).isoformat()})
        status['cleanupVerified'] = not members and not leftovers
        status['inventoryAfter'] = inventory(root) == p['buildInventory']
        if status['exitCode'] == 0 and not status['cancelled'] and status['inventoryAfter'] and status['cleanupVerified']:
            status['status'] = 'success'
    except Exception as error:
        status['errorType'] = type(error).__name__; status['error'] = str(error)
    finally:
        status['producerSeconds'] = time.monotonic() - start
        status['finishedAt'] = datetime.now(timezone.utc).isoformat()
        if status['cancelled']: status['status'] = 'cancelled'
        status['originalArtifacts'] = {str(path.relative_to(out)): sha(path) for path in sorted(out.rglob('*')) if path.is_file()}
        write(out / 'producer.json', status)
        for sig, handler in handlers.items(): signal.signal(sig, handler)
    return 0 if status['status'] == 'success' else 1

def validate_producers(p, dirs, prerequisite_statuses, mode='prototype'):
    verify_plan(p)
    names = ['baseline'] if mode == 'baseline' else ['shard-1', 'shard-2']
    expected_rows = [p['discovery']['tests']] if mode == 'baseline' else p['shards']
    if prerequisite_statuses != ['success'] * len(names): raise ValueError('Failed/cancelled/skipped/missing producer prerequisite')
    if len(dirs) != len(names): raise ValueError('Missing original producer')
    all_names = Counter()
    for producer, rows, directory in zip(names, expected_rows, map(Path, dirs)):
        report = load(directory / 'producer.json')
        binding = {'runId': p['runId'], 'producer': producer, 'sourceHead': p['sourceHead'],
                   'planSha256': p['planSha256'], 'buildSha256': p['buildSha256'], 'sdk': p['sdk'],
                   'sdkFingerprint': p['sdkFingerprint'], 'status': 'success', 'exitCode': 0,
                   'inventoryBefore': True, 'inventoryAfter': True, 'cancelled': False,
                   'cleanupVerified': True, 'filteredDiscoveryMatches': True}
        for key, value in binding.items():
            if report.get(key) != value: raise ValueError('Non-success/stale/mismatched producer: ' + key)
        required = {'original.log', 'original.trx', 'original.coverage', 'coverage.cobertura.xml', 'coverage-export-original.log', 'filtered-discovery-original.json',
                    'cleanup.json', 'command.json', 'docker-before.json'}
        artifacts = report.get('originalArtifacts', {})
        actual = {str(path.relative_to(directory)): sha(path) for path in directory.rglob('*') if path.is_file() and path.name != 'producer.json'}
        if not required <= set(artifacts) or actual != artifacts: raise ValueError('Missing/changed original artifacts')
        if report.get('coverageTool', '').split('+')[0] != '18.12.0': raise ValueError('Unpinned original coverage exporter')
        if 'FIXTURE_COST_PROBE_INVALID' in (directory / 'original.log').read_text(): raise ValueError('Invalid phase probe')
        discovered = discovery_rows(load(directory / 'filtered-discovery-original.json'))
        if Counter(canonical(row) for row in discovered) != Counter(canonical(row) for row in rows): raise ValueError('Filtered discovery differs')
        cleanup = load(directory / 'cleanup.json')
        if cleanup['remainingProcesses'] or cleanup['additionalContainers']: raise ValueError('Resource cleanup incomplete')
        cases = results(directory / 'original.trx'); expected = Counter(row['displayName'] for row in rows)
        if Counter(case['name'] for case in cases) != expected: raise ValueError('Missing/duplicate/stale cases')
        if Counter(case['outcome'] for case in cases) != {'Passed': len(cases)}: raise ValueError('Failed/skipped/inconclusive case')
        coverage_gate(directory / 'coverage.cobertura.xml', threshold=0)
        all_names.update(case['name'] for case in cases)
    if all_names != Counter(row['displayName'] for row in p['discovery']['tests']): raise ValueError('Combined cases differ')

PRODUCT_MODULES = {'ApexRacers.Api', 'ApexRacers.Core', 'ApexRacers.Data', 'ApexRacers.Ingestion', 'ApexRacers.Seeder'}


def coverage_gate(path, threshold=.85):
    import math
    root = ET.parse(path).getroot()
    packages = root.findall('packages/package')
    names = [package.get('name') for package in packages]
    if set(names) != PRODUCT_MODULES or len(names) != len(PRODUCT_MODULES):
        raise ValueError('Missing/duplicate/non-product coverage module')
    for prefix, rate_name in [('lines', 'line-rate'), ('branches', 'branch-rate')]:
        valid = int(root.get(prefix + '-valid', '0')); covered = int(root.get(prefix + '-covered', '-1'))
        rate = float(root.get(rate_name, 'nan'))
        if valid <= 0 or not 0 <= covered <= valid or not math.isfinite(rate) or not 0 <= rate <= 1:
            raise ValueError('Empty/inconsistent/nonfinite coverage counters')
        if not math.isclose(rate, covered / valid, rel_tol=1e-10, abs_tol=1e-10):
            raise ValueError('Reported coverage rate differs from original counters')
        if rate < threshold: raise ValueError('Coverage below existing85% gates')
    return {name: root.get(name) for name in ('lines-valid', 'branches-valid', 'lines-covered', 'branches-covered', 'line-rate', 'branch-rate')}

def coverage_model(path):
    root = ET.parse(path).getroot()
    model = []
    for package in root.findall('packages/package'):
        for cls in package.findall('classes/class'):
            filename = cls.attrib['filename'].replace('\\', '/')
            # Reports use checkout-absolute filenames; the bound source bytes own the identity.
            if '/src/' in filename: filename = 'src/' + filename.split('/src/', 1)[1]
            for line in cls.findall('lines/line'):
                branches = []
                for condition in line.findall('conditions/condition'):
                    branches.append((condition.get('number'), condition.get('type')))
                branch = line.get('branch', 'false').lower()
                denominator = None
                if branch == 'true':
                    match = re.fullmatch(r'[^()]+\((\d+)/(\d+)\)', line.get('condition-coverage', ''))
                    if not match or int(match[2]) <= 0: raise ValueError('Missing original branch-line denominator')
                    denominator = int(match[2])
                model.append((package.attrib['name'], cls.attrib['name'], filename, line.attrib['number'],
                              branch, denominator, tuple(sorted(branches))))
    if not model: raise ValueError('Empty product source/line model')
    return (int(root.attrib['lines-valid']), int(root.attrib['branches-valid']), tuple(sorted(model)))


def aggregate(args):
    p = load(args.plan); validate_producers(p, args.producers, args.statuses, args.mode)
    args.output.mkdir(parents=True, exist_ok=False)
    reports = [directory / 'coverage.cobertura.xml' for directory in args.producers]
    if args.mode == 'prototype':
        if coverage_model(reports[0]) != coverage_model(reports[1]):
            raise ValueError('Shard product denominators/source/branch models differ')
        version = subprocess.check_output([str(args.tool), '--version'], text=True).strip()
        if version.split('+')[0].split()[0] != '18.12.0': raise ValueError('Unpinned coverage merge tool')
        report = args.output / 'merged.cobertura.xml'
        with (args.output / 'merge.log').open('w') as log:
            subprocess.run([str(args.tool), 'merge', *[str(directory / 'original.coverage') for directory in args.producers], '-f', 'cobertura', '-o', str(report)],
                           stdout=log, stderr=subprocess.STDOUT, check=True)
        if coverage_model(report) != coverage_model(reports[0]): raise ValueError('Merge changed product coverage model')
    else:
        version = None; report = reports[0]
    gates = coverage_gate(report)
    if args.reference_baseline:
        reference = load(args.reference_baseline / 'accepted.json')
        if reference.get('mode') != 'baseline' or reference.get('buildSha256') != p['buildSha256'] or reference.get('sdkFingerprint') != p['sdkFingerprint']:
            raise ValueError('Reference baseline source/build/SDK differs')
        baseline_path = args.reference_baseline / reference['coverageFile']
        if sha(baseline_path) != reference['coverageSha256'] or coverage_model(baseline_path) != coverage_model(report):
            raise ValueError('Reference baseline denominator/model differs or original changed')
    # Recheck every original after the supported merge; never substitute a later passing result.
    validate_producers(p, args.producers, args.statuses, args.mode)
    if args.mode == 'baseline':
        # A copy is for portable comparison only; its hash remains bound to the original report.
        shutil.copyfile(report, args.output / 'baseline.cobertura.xml'); report = args.output / 'baseline.cobertura.xml'
    write(args.output / 'accepted.json', {'runId': p['runId'], 'mode': args.mode, 'sourceHead': p['sourceHead'],
          'planSha256': p['planSha256'], 'buildSha256': p['buildSha256'], 'sdkFingerprint': p['sdkFingerprint'],
          'originalCases': len(p['discovery']['tests']), 'coverage': gates, 'mergeTool': version,
          'coverageFile': report.name, 'coverageSha256': sha(report), 'liveEligible': False})


def main():
    parser = argparse.ArgumentParser(description=__doc__); cmds = parser.add_subparsers(dest='command', required=True)
    p = cmds.add_parser('plan')
    p.add_argument('--root', type=Path, required=True); p.add_argument('--discovery', type=Path, required=True)
    p.add_argument('--weights', type=Path, required=True); p.add_argument('--run-id', required=True); p.add_argument('--output', type=Path, required=True)
    p = cmds.add_parser('run')
    p.add_argument('--root', type=Path, required=True); p.add_argument('--plan', type=Path, required=True)
    p.add_argument('--producer', choices=['baseline', 'shard-1', 'shard-2'], required=True); p.add_argument('--tool', type=Path, required=True); p.add_argument('--output', type=Path, required=True)
    p = cmds.add_parser('aggregate')
    p.add_argument('--plan', type=Path, required=True); p.add_argument('--mode', choices=['baseline', 'prototype'], required=True)
    p.add_argument('--producers', type=Path, nargs='+', required=True); p.add_argument('--statuses', nargs='+', required=True)
    p.add_argument('--tool', type=Path); p.add_argument('--reference-baseline', type=Path); p.add_argument('--output', type=Path, required=True)
    args = parser.parse_args()
    if args.command == 'aggregate' and args.mode == 'prototype' and args.tool is None: parser.error('Prototype requires pinned merge --tool')
    return {'plan': plan, 'run': execute, 'aggregate': aggregate}[args.command](args) or 0


if __name__ == '__main__': raise SystemExit(main())
