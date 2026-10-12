#!/usr/bin/env python3
"""One original full MTP control and one shared live collection; never changes CI."""
import argparse
from collections import Counter, defaultdict
import importlib.util
import json
import math
import os
from pathlib import Path
import shutil
import signal
import statistics
import subprocess
import sys
import tempfile
import time
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[1]


def module(name, file):
    spec = importlib.util.spec_from_file_location(name, ROOT / 'scripts' / file)
    value = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(value)
    return value


cost = module('cost', 'measure-backend-cost.py')
probe = module('probe', 'probe-coverage-preservation.py')


def write(path, value):
    path.write_text(json.dumps(value, indent=2) + '\n')


def docker_ids():
    return sorted(subprocess.check_output(['docker', 'ps', '-aq'], text=True).split())


def test_environment(directory):
    env = os.environ.copy()
    # MTP uses Unix sockets under TMPDIR; the workspace path exceeds their limit.
    temporary = tempfile.mkdtemp(prefix='ax408-')
    env['TMPDIR'] = temporary
    env['TESTINGPLATFORM_PIPE_DIRECTORY'] = temporary
    for key, leaf in [('DOTNET_CLI_HOME', 'cli'), ('NUGET_PACKAGES', 'nuget'),
                      ('MSBuildUserExtensionsPath', 'msbuild'), ('DRIVER_PRIVACY_EVIDENCE_DIR', 'evidence'),
                      ('APEXRACERS_COST_PROBE_DIR', 'phases'), ('PUBLICATION_REHEARSAL_ARTIFACTS', 'publication')]:
        target = directory / leaf
        target.mkdir(parents=True, exist_ok=False)
        env[key] = str(target)
    write(directory / 'environment-paths-original.json',
          {key: env[key] for key in ('TMPDIR', 'TESTINGPLATFORM_PIPE_DIRECTORY', 'DOTNET_CLI_HOME',
                                    'NUGET_PACKAGES', 'MSBuildUserExtensionsPath', 'DRIVER_PRIVACY_EVIDENCE_DIR',
                                    'APEXRACERS_COST_PROBE_DIR', 'PUBLICATION_REHEARSAL_ARTIFACTS')})
    return env


def invocation(executable, directory, uids=None):
    command = [str(executable), '--report-xunit-trx', '--report-xunit-trx-filename', 'original.trx',
               '--results-directory', str(directory)]
    if uids is not None:
        command += ['--filter-uid', *uids]
    return command


def shared_child(config_file):
    config = json.loads(Path(config_file).read_text())
    children = []
    for item in config:
        directory = Path(item['directory'])
        env = test_environment(directory)
        log = (directory / 'original.log').open('w')
        command = invocation(Path(item['executable']), directory, item['uids'])
        write(directory / 'command-original.json', command)
        process = subprocess.Popen(command, cwd=ROOT, env=env, stdout=log, stderr=subprocess.STDOUT)
        children.append((directory, process, log))
    codes = []
    for directory, process, log in children:
        code = process.wait()
        log.close()
        write(directory / 'original-exit.json', {'exitCode': code})
        codes.append(code)
    return 0 if codes == [0, 0] else 1


def plan_shards(rows):
    if not rows or len({r['uid'] for r in rows}) != len(rows) or len({r['displayName'] for r in rows}) != len(rows):
        raise ValueError('Empty/ambiguous original execution inventory')
    historic = json.loads((ROOT / 'docs/testing/backend-fixture-cost.json').read_text())
    methods = defaultdict(list)
    for run in historic['runs']:
        if str(run['run']).endswith('-coverage'):
            for method, data in run['methods'].items():
                methods[method.rsplit('.', 1)[0]].append((method, data['trxSeconds']))
    groups = defaultdict(list)
    for row in rows:
        kind = row['type']
        groups[kind['namespace'] + '.' + kind['typeName']].append(row)
    weights = {}
    for name, cases in groups.items():
        values = defaultdict(list)
        for method, seconds in methods[name]:
            values[method].append(seconds)
        weights[name] = sum(statistics.median(v) for v in values.values()) if values else .05 * len(cases)
    bins, totals = [[], []], [0., 0.]
    for name in sorted(groups, key=lambda c: (-weights[c], c)):
        index = min(range(2), key=lambda i: (totals[i], i))
        bins[index].extend(groups[name])
        totals[index] += weights[name]
    return bins, weights


def normalized_report(path, hits=False):
    root = ET.parse(path).getroot()
    rows = []
    for package in root.findall('packages/package'):
        for cls in package.findall('classes/class'):
            filename = cls.attrib['filename'].replace('\\', '/')
            if '/src/' in filename:
                filename = 'src/' + filename.split('/src/', 1)[1]
            for method in cls.findall('methods/method'):
                for line in method.findall('lines/line'):
                    conditions = tuple(sorted((c.get('number'), c.get('type'), c.get('coverage') if hits else None)
                                              for c in line.findall('conditions/condition')))
                    denominator = line.get('condition-coverage', '').split('/')[-1].rstrip(')')
                    rows.append((package.attrib['name'], cls.attrib['name'], filename, method.get('name'),
                                 method.get('signature'), line.get('number'), line.get('branch'), denominator,
                                 conditions, int(line.get('hits', '0')) > 0 if hits else None,
                                 line.get('condition-coverage') if hits else None))
            for line in cls.findall('lines/line'):
                conditions = tuple(sorted((c.get('number'), c.get('type'), c.get('coverage') if hits else None)
                                          for c in line.findall('conditions/condition')))
                denominator = line.get('condition-coverage', '').split('/')[-1].rstrip(')')
                rows.append((package.attrib['name'], cls.attrib['name'], filename, '@class', '',
                             line.get('number'), line.get('branch'), denominator, conditions,
                             int(line.get('hits', '0')) > 0 if hits else None,
                             line.get('condition-coverage') if hits else None))
    return sorted(rows)


def coverage_gate(path):
    r = ET.parse(path).getroot()
    modules = [p.attrib['name'] for p in r.findall('packages/package')]
    if set(modules) != {'ApexRacers.Api', 'ApexRacers.Core', 'ApexRacers.Data', 'ApexRacers.Ingestion', 'ApexRacers.Seeder'} or len(modules) != 5:
        raise ValueError('Missing, duplicate or non-product modules')
    for prefix in ('lines', 'branches'):
        valid, covered = int(r.get(prefix + '-valid', '0')), int(r.get(prefix + '-covered', '0'))
        if valid <= 0 or not 0 <= covered <= valid or covered / valid < .85:
            raise ValueError('Nonpositive/invalid/sub85% original product denominator')
        rate = float(r.get('line-rate' if prefix == 'lines' else 'branch-rate', 'nan'))
        if not math.isfinite(rate) or not math.isclose(rate, covered / valid, abs_tol=1e-10):
            raise ValueError('Invalid or inconsistent reported coverage rate')
    return r.attrib


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--tool', type=Path, required=True)
    parser.add_argument('--output', type=Path, required=True)
    args = parser.parse_args()
    def cancelled(*_):
        raise KeyboardInterrupt()
    signal.signal(signal.SIGTERM, cancelled)
    out = args.output.resolve()
    out.mkdir(parents=True, exist_ok=False)
    tool = args.tool.resolve()
    shutil.copy2(__file__, out / 'runner-original.py')
    version = subprocess.check_output([str(tool), '--version'], text=True).strip()
    if not version.startswith('18.12.0+'):
        raise ValueError('Unpinned collector')
    context = {'schemaVersion': 1, 'sourceHead': subprocess.check_output(['git', 'rev-parse', 'HEAD'], cwd=ROOT, text=True).strip(),
               'toolVersion': version, 'commands': [], 'liveEligible': False, 'adoptionEligible': False}

    def persist():
        write(out / 'context.json', context)

    def run(label, command, env=None):
        directory = out / label
        directory.mkdir(exist_ok=True)
        write(directory / 'command-original.json', [str(x) for x in command])
        started = time.monotonic()
        with (directory / 'original.log').open('w') as log:
            process = subprocess.Popen([str(x) for x in command], cwd=ROOT, env=env, stdout=log,
                                       stderr=subprocess.STDOUT, start_new_session=True)
            try:
                code = process.wait()
            except BaseException:
                os.killpg(process.pid, signal.SIGTERM)
                try:
                    process.wait(timeout=30)
                except subprocess.TimeoutExpired:
                    os.killpg(process.pid, signal.SIGKILL)
                    process.wait()
                context['commands'].append({'label': label, 'exitCode': process.returncode,
                                            'seconds': time.monotonic() - started, 'processGroup': process.pid,
                                            'interrupted': True})
                persist()
                raise
        record = {'label': label, 'exitCode': code, 'seconds': time.monotonic() - started, 'processGroup': process.pid}
        context['commands'].append(record)
        persist()
        if code:
            raise ValueError('Original command failed; no later invocation substitutes for ' + label)

    persist()
    run('build', ['dotnet', 'build', '--configuration', 'Release'])
    context['sourceBuildBefore'] = cost.inventory()
    context['sdkBefore'] = cost.sdk_inventory()
    material = [Path(__file__).resolve(), ROOT / 'scripts/probe-coverage-preservation.py',
                ROOT / 'scripts/measure-backend-cost.py', ROOT / 'docs/testing/backend-fixture-cost.json',
                ROOT / 'ApexRacers.sln']
    context['researchInputsBefore'] = {str(p.relative_to(ROOT)): cost.digest(p) for p in material}
    runtimes = Path(shutil.which('dotnet')).resolve().parent / 'shared'
    context['runtimeBefore'] = probe.inventory(runtimes)
    store = tool.parent / '.store/dotnet-coverage/18.12.0'
    context['toolBefore'] = probe.inventory(store)
    context['dockerBefore'] = docker_ids()
    executable = cost.TEST_OUTPUT / 'ApexRacers.Tests'
    run('discovery', [str(executable), '--list-tests', 'json'])
    discovery = (out / 'discovery/original.log').read_text()
    (out / 'discovery-original.json').write_text(discovery)
    rows = json.loads(discovery)['tests']
    shards, weights = plan_shards(rows)
    write(out / 'partition.json', {'shards': shards, 'weights': weights, 'scope': 'Historical scheduling weights only; no CI timing claim'})
    baseline = out / 'baseline'
    baseline.mkdir()
    baseline_env = test_environment(baseline)
    command = invocation(executable, baseline) + ['--coverage', '--coverage-output', 'original.xml', '--coverage-output-format',
                                                  'cobertura', '--coverage-settings', str(ROOT / 'coverage.runsettings')]
    persist()
    run('baseline', command, baseline_env)
    coverage_gate(baseline / 'original.xml')
    if cost.read_results(baseline / 'original.trx') != (Counter(r['displayName'] for r in rows), Counter({'Passed': len(rows)})):
        raise ValueError('Baseline original inventory/outcomes differ')
    if cost.inventory() != context['sourceBuildBefore'] or cost.sdk_inventory() != context['sdkBefore']:
        raise ValueError('Material inputs changed during baseline')
    shared = out / 'shared'
    shared.mkdir()
    settings = ET.parse(ROOT / 'coverage.runsettings').getroot()
    for name, value in [('IncludeTestAssembly', 'False'), ('ExcludeAssembliesWithoutSources', 'MissingAll')]:
        ET.SubElement(settings, name).text = value
    for name, value in [('UseVerifiableInstrumentation', 'False'), ('EnableStaticManagedInstrumentation', 'False'),
                        ('EnableDynamicManagedInstrumentation', 'True'), ('EnableStaticNativeInstrumentation', 'False'),
                        ('EnableDynamicNativeInstrumentation', 'False'), ('CollectFromChildProcesses', 'True')]:
        ET.SubElement(settings.find('CodeCoverage'), name).text = value
    ET.ElementTree(settings).write(shared / 'settings-original.config', encoding='utf-8', xml_declaration=True)
    children = []
    for index, shard in enumerate(shards, 1):
        directory = shared / f'shard-{index}'
        directory.mkdir()
        copy = directory / 'build'
        shutil.copytree(cost.TEST_OUTPUT, copy)
        if probe.inventory(copy) != probe.inventory(cost.TEST_OUTPUT):
            raise ValueError('Independent copied build differs')
        children.append({'directory': str(directory), 'executable': str(copy / 'ApexRacers.Tests'), 'uids': [r['uid'] for r in shard]})
    write(shared / 'children-original.json', children)
    run('shared', [tool, 'collect', '--settings', shared / 'settings-original.config', '--output-format', 'cobertura',
                   '--output', shared / 'original.xml', 'python3', Path(__file__).resolve(), '--child', shared / 'children-original.json'])
    for index, shard in enumerate(shards, 1):
        directory = shared / f'shard-{index}'
        if cost.read_results(directory / 'original.trx') != (Counter(r['displayName'] for r in shard), Counter({'Passed': len(shard)})):
            raise ValueError('Shard original inventory/outcomes differ')
        if probe.inventory(directory / 'build') != probe.inventory(cost.TEST_OUTPUT):
            raise ValueError('Producer build changed')
    context['baselineCoverage'] = coverage_gate(baseline / 'original.xml')
    context['sharedCoverage'] = coverage_gate(shared / 'original.xml')
    context['modelEqual'] = normalized_report(baseline / 'original.xml') == normalized_report(shared / 'original.xml')
    context['denominatorsEqual'] = all(context['baselineCoverage'][key] == context['sharedCoverage'][key]
                                     for key in ('lines-valid', 'branches-valid'))
    context['perMethodObservationsEqual'] = normalized_report(baseline / 'original.xml', True) == normalized_report(shared / 'original.xml', True)
    context['coveredCountsEqual'] = all(context['baselineCoverage'][key] == context['sharedCoverage'][key]
                                      for key in ('lines-covered', 'branches-covered'))
    context['sourceBuildUnchanged'] = cost.inventory() == context['sourceBuildBefore']
    context['sdkUnchanged'] = cost.sdk_inventory() == context['sdkBefore']
    context['toolUnchanged'] = probe.inventory(store) == context['toolBefore']
    context['runtimeUnchanged'] = probe.inventory(runtimes) == context['runtimeBefore']
    context['researchInputsUnchanged'] = {str(p.relative_to(ROOT)): cost.digest(p) for p in material} == context['researchInputsBefore']
    context['dockerAfter'] = docker_ids()
    context['additionalContainers'] = sorted(set(context['dockerAfter']) - set(context['dockerBefore']))
    context['originalArtifacts'] = {str(p.relative_to(out)): cost.digest(p) for p in out.rglob('*') if p.is_file() and p.name != 'context.json'}
    context['preservationDemonstrated'] = all(context[k] for k in ('modelEqual', 'denominatorsEqual', 'perMethodObservationsEqual', 'coveredCountsEqual', 'sourceBuildUnchanged', 'sdkUnchanged', 'toolUnchanged', 'runtimeUnchanged', 'researchInputsUnchanged')) and not context['additionalContainers']
    persist()
    if not context['preservationDemonstrated']:
        raise ValueError('Original comparison failed; evidence retained, no adoption')
    print(out / 'context.json')


if __name__ == '__main__':
    if len(sys.argv) == 3 and sys.argv[1] == '--child':
        raise SystemExit(shared_child(sys.argv[2]))
    main()
