#!/usr/bin/env python3
"""Bounded #411 research: preserve original reports and compare await observations."""
import argparse
from collections import Counter
import importlib.util
import json
from pathlib import Path
import hashlib
import sys
import os
import shutil
import subprocess
import tempfile
import time
import signal

ROOT = Path(__file__).resolve().parents[1]
spec = importlib.util.spec_from_file_location('product411', ROOT / 'scripts/probe-product-coverage-preservation.py')
product = importlib.util.module_from_spec(spec)
spec.loader.exec_module(product)


def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def target_rows(path, hits=True):
    return [row for row in product.normalized_report(path, hits)
            if row[0] == 'ApexRacers.Api' and row[1].startswith('ApexRacers.Api.Services.AdminService.<GetFlagsFor')
            and row[6] == 'True']


def compare(baseline, candidate):
    model_equal = product.normalized_report(baseline) == product.normalized_report(candidate)
    observed_equal = product.normalized_report(baseline, True) == product.normalized_report(candidate, True)
    roots = [product.ET.parse(path).getroot().attrib for path in (baseline, candidate)]
    counts_equal = all(roots[0][key] == roots[1][key]
                       for key in ('lines-valid', 'lines-covered', 'branches-valid', 'branches-covered'))
    a, b = target_rows(baseline), target_rows(candidate)
    return {'modelEqual': model_equal, 'observationsEqual': observed_equal, 'countsEqual': counts_equal,
            'baselineTargetRows': a, 'candidateTargetRows': b,
            'baselineOnlyRows': sorted(set(a) - set(b)), 'candidateOnlyRows': sorted(set(b) - set(a)),
            'baselineRoot': roots[0], 'candidateRoot': roots[1]}


def helper(name, filename):
    spec = importlib.util.spec_from_file_location(name, ROOT / 'scripts' / filename)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def shared_settings(path):
    settings = product.ET.parse(ROOT / 'coverage.runsettings').getroot()
    for name, value in [('IncludeTestAssembly', 'False'), ('ExcludeAssembliesWithoutSources', 'MissingAll')]:
        product.ET.SubElement(settings, name).text = value
    for name, value in [('UseVerifiableInstrumentation', 'False'), ('EnableStaticManagedInstrumentation', 'False'),
                        ('EnableDynamicManagedInstrumentation', 'True'), ('EnableStaticNativeInstrumentation', 'False'),
                        ('EnableDynamicNativeInstrumentation', 'False'), ('CollectFromChildProcesses', 'True')]:
        product.ET.SubElement(settings.find('CodeCoverage'), name).text = value
    product.ET.ElementTree(settings).write(path, encoding='utf-8', xml_declaration=True)
    return path


def input_bindings(paths):
    return {str(path.resolve()): digest(path) for path in paths}


def child(config):
    data = json.loads(Path(config).read_text())
    children = []
    def start(item):
        directory = Path(item['directory'])
        env = product.test_environment(directory)
        command = product.invocation(Path(item['executable']), directory, item['uids'])
        product.write(directory / 'command-original.json', command)
        log = (directory / 'original.log').open('w')
        process = subprocess.Popen(command, cwd=ROOT, env=env, stdout=log, stderr=subprocess.STDOUT)
        return process, directory, log
    def wait(item):
        process, directory, log = item
        code = process.wait()
        log.close()
        product.write(directory / 'original-exit.json', {'exitCode': code})
        return code
    if data['parallel']:
        children = [start(item) for item in data['producers']]
        codes = [wait(item) for item in children]
    else:
        codes = [wait(start(item)) for item in data['producers']]
    return 0 if all(code == 0 for code in codes) else 1


def focus(output, tool, build_root):
    owner_module = helper('owner411', 'backend-shared-shards.py')
    discovery_module = helper('mapping411', 'probe-shared-product-coverage.py')
    ownership = owner_module.Ownership([], output / 'ownership-original.jsonl')
    tool = tool.resolve()
    if not subprocess.check_output([str(tool), '--version'], text=True).strip().startswith('18.12.0+'):
        raise ValueError('Unpinned collector')
    context = {'schemaVersion': 1, 'scope': 'Focused unchanged AdminServiceTests; sequential/parallel repetitions are declared diagnostic workloads, not sharding acceptance or retries',
               'sourceHead': subprocess.check_output(['git', 'rev-parse', 'HEAD'], cwd=ROOT, text=True).strip(),
               'commands': [], 'comparisons': [], 'liveEligible': False, 'adoptionEligible': False}
    canonical = output / 'canonical'
    shutil.copytree(build_root, canonical)
    inputs = [Path(__file__).resolve(), ROOT / 'scripts/backend-shared-shards.py',
              ROOT / 'scripts/probe-product-coverage-preservation.py', ROOT / 'scripts/probe-shared-product-coverage.py',
              ROOT / 'scripts/probe-coverage-preservation.py', ROOT / 'scripts/measure-backend-cost.py']
    for path in inputs:
        shutil.copy2(path, output / ('helper-original-' + path.name))
    def capture():
        return {'sourceBuild': product.cost.inventory(), 'canonical': product.probe.inventory(canonical),
                'sdk': product.cost.sdk_inventory(), 'runtime': product.probe.inventory(Path(shutil.which('dotnet')).resolve().parent / 'shared'),
                'tool': product.probe.inventory(tool.parent / '.store/dotnet-coverage/18.12.0')}
    context['before'] = capture()
    deps = json.loads((canonical / 'ApexRacers.Tests.deps.json').read_text())
    context['packageBindings'] = [key for key in deps['libraries'] if 'xunit.v3.mtp' in key or 'CodeCoverage' in key]
    product.write(output / 'context.json', context)
    def run(label, command, env=None):
        directory = output / label
        directory.mkdir(exist_ok=True)
        product.write(directory / 'command-original.json', list(map(str, command)))
        started = time.monotonic()
        with (directory / 'original.log').open('w') as log:
            process = subprocess.Popen(list(map(str, command)), cwd=ROOT, env=env, stdout=log, stderr=subprocess.STDOUT, start_new_session=True)
            ownership.attach(process.pid)
            try:
                while process.poll() is None:
                    if time.monotonic() - started > 120: raise TimeoutError('Focused original exceeded120seconds')
                    ownership.observe()
                    time.sleep(.2)
                code = process.wait()
            except BaseException:
                ownership.terminate(signal.SIGTERM)
                try: process.wait(timeout=5)
                except subprocess.TimeoutExpired:
                    ownership.terminate(signal.SIGKILL)
                    process.wait()
                raise
        context['commands'].append({'label': label, 'argv': list(map(str, command)), 'exitCode': code, 'seconds': time.monotonic() - started})
        product.write(output / 'context.json', context)
        if code:
            raise ValueError('Original failed invocation: ' + label)
        return directory
    try:
        discovery = run('discovery', [canonical / 'ApexRacers.Tests', '--list-tests', 'json'])
        allrows = json.loads((discovery / 'original.log').read_text())['tests']
        expected = [row for row in allrows if row['type']['namespace'] + '.' + row['type']['typeName'] == 'ApexRacers.Tests.Services.AdminServiceTests']
        if not expected:
            raise ValueError('Focused class not discovered')
        context['executionsPerProducer'] = len(expected)
        product.write(output / 'cases-original.json', expected)
        shared_settings(output / 'shared-settings-original.config')
        context['inputsBefore'] = input_bindings([*inputs, ROOT / 'coverage.runsettings', output / 'shared-settings-original.config'])
        reports = []
        reports = []
        for mode in ('mtp', 'shared-single', 'shared-sequential', 'shared-parallel', 'mtp-repeat'):
            directory = output / mode
            directory.mkdir()
            producers = []
            for index in range(2 if mode in ('shared-sequential', 'shared-parallel') else 1):
                producer = directory / f'producer-{index + 1}'
                producer.mkdir()
                ownership.directories.append(producer)
                build = producer / 'build'
                shutil.copytree(canonical, build)
                if product.probe.inventory(build) != context['before']['canonical']:
                    raise ValueError('Copied binaries changed')
                discovered = run(mode + f'-discovery-{index + 1}', [build / 'ApexRacers.Tests', '--list-tests', 'json'])
                mapping = discovery_module.map_discovery(allrows, json.loads((discovered / 'original.log').read_text())['tests'])
                producers.append({'directory': str(producer), 'executable': str(build / 'ApexRacers.Tests'), 'uids': [mapping[row['displayName']] for row in expected]})
                product.write(producer / 'mapping-original.json', [{'displayName': row['displayName'], 'canonicalUid': row['uid'], 'copiedUid': mapping[row['displayName']]} for row in expected])
            report = directory / 'original.xml'
            if mode.startswith('mtp'):
                producer = Path(producers[0]['directory'])
                command = product.invocation(Path(producers[0]['executable']), producer, producers[0]['uids'])
                command += ['--coverage', '--coverage-output', str(report), '--coverage-output-format', 'cobertura', '--coverage-settings', str(ROOT / 'coverage.runsettings')]
                run(mode, command, product.test_environment(producer))
            else:
                config = directory / 'children-original.json'
                product.write(config, {'producers': producers, 'parallel': mode == 'shared-parallel'})
                command = [str(tool), 'collect', '--settings', str(output / 'shared-settings-original.config'), '--output-format', 'cobertura', '--output', str(report),
                           'python3', str(Path(__file__).resolve()), '--children', str(config)]
                run(mode, command)
            for item in producers:
                producer = Path(item['directory'])
                if product.cost.read_results(producer / 'original.trx') != (Counter(r['displayName'] for r in expected), Counter({'Passed': len(expected)})):
                    raise ValueError('Missing/duplicate/non-passing original focused cases')
                if product.probe.inventory(producer / 'build') != context['before']['canonical']:
                    raise ValueError('Producer binaries changed')
            context['comparisons'].append({'mode': mode, 'targetRows': target_rows(report), 'reportSha256': digest(report),
                                           'declaredExecutions': len(expected) * len(producers), 'allPassed': True,
                                           'againstFirstMtp': compare(reports[0], report) if reports else None})
            reports.append(report)
            product.write(output / 'context.json', context)
        context['after'] = capture()
        context['inputsAfter'] = input_bindings([*inputs, ROOT / 'coverage.runsettings', output / 'shared-settings-original.config'])
        context['materialBindingsUnchanged'] = context['before'] == context['after'] and context['inputsBefore'] == context['inputsAfter']
        if not context['materialBindingsUnchanged']:
            raise ValueError('Source/build/SDK/tool/runtime bytes changed')
    except BaseException as error:
        context['error'] = {'type': type(error).__name__, 'message': str(error)}
    finally:
        try: context['cleanup'] = ownership.cleanup()
        except BaseException as error: context['cleanup'] = {'complete': False, 'errorType': type(error).__name__, 'message': str(error)}
        context['originalFiles'] = {str(path.relative_to(output)): digest(path) for path in output.rglob('*') if path.is_file()
                                    and path.name != 'context.json' and not any(part in ('build', 'canonical', 'nuget', 'cli', 'msbuild') for part in path.relative_to(output).parts)}
        product.write(output / 'context.json', context)
    print(output / 'context.json')
    return 1 if context.get('error') or not context['cleanup']['complete'] else 0


def diagnostic(output, tool, canonical, fixture, modes=None, call_order="user-first"):
    owners = helper('ownerDiagnostic411', 'backend-shared-shards.py')
    canonical, fixture, tool = canonical.resolve(), fixture.resolve(), tool.resolve()
    context = {'schemaVersion': 1, 'scope': 'Standalone actual AdminService/EF/SQLite and real PostgreSQL queries; controlled pending query, no product source modification',
               'commands': [], 'results': [], 'callOrder': call_order, 'liveEligible': False, 'adoptionEligible': False}
    inputs = input_bindings([Path(__file__).resolve(), ROOT / 'scripts/async-coverage-probe/Program.cs',
                             ROOT / 'scripts/async-coverage-probe/Probe.csproj', ROOT / 'scripts/backend-shared-shards.py',
                             ROOT / 'scripts/probe-product-coverage-preservation.py', ROOT / 'scripts/probe-shared-product-coverage.py',
                             ROOT / 'scripts/probe-coverage-preservation.py', ROOT / 'scripts/measure-backend-cost.py'])
    def snapshot():
        return {'canonical': product.probe.inventory(canonical), 'fixture': product.probe.inventory(fixture),
                'sdk': product.cost.sdk_inventory(), 'runtime': product.probe.inventory(Path(shutil.which('dotnet')).resolve().parent / 'shared'),
                'tool': product.probe.inventory(tool.parent / '.store/dotnet-coverage/18.12.0')}
    context['before'] = snapshot()
    for name, expected in context['before']['canonical'].items():
        if '/' not in name and name.endswith(('.dll', '.pdb')):
            if context['before']['fixture'].get(name) != expected:
                raise ValueError('Diagnostic references changed canonical dependency bytes: ' + name)
    context['inputsBefore'] = inputs
    for path in inputs: shutil.copy2(path, output / ('source-original-' + Path(path).name))
    product.write(output / 'context.json', context)
    owner = owners.Ownership([], output / 'ownership-original.jsonl')
    settings = shared_settings(output / 'shared-settings-original.config')
    inputs[str(settings)] = digest(settings)
    inputs[str(ROOT / 'coverage.runsettings')] = digest(ROOT / 'coverage.runsettings')
    context['inputsBefore'] = inputs
    product.write(output / 'settings-binding-original.json', {'path': str(settings), 'sha256': digest(settings)})
    def command(mode, directory):
        return ['dotnet', 'exec', '--depsfile', str(canonical / 'ApexRacers.Tests.deps.json'),
                '--runtimeconfig', str(canonical / 'ApexRacers.Tests.runtimeconfig.json'), str(fixture / 'AsyncCoverageDiagnostic.dll'),
                mode, str(directory / 'observations-original.json'), call_order]
    def run(label, argv, directory, env):
        owner.directories.append(directory)
        product.write(directory / 'command-original.json', argv)
        start = time.monotonic()
        with (directory / 'original.log').open('w') as log:
            process = subprocess.Popen(argv, cwd=ROOT, env=env, stdout=log, stderr=subprocess.STDOUT, start_new_session=True)
            owner.attach(process.pid)
            try:
                while process.poll() is None:
                    if time.monotonic() - start > 120: raise TimeoutError('Diagnostic original exceeded120seconds')
                    owner.observe()
                    time.sleep(.2)
                code = process.wait()
            except BaseException:
                owner.terminate(signal.SIGTERM)
                try: process.wait(timeout=5)
                except subprocess.TimeoutExpired:
                    owner.terminate(signal.SIGKILL)
                    process.wait()
                raise
        context['commands'].append({'label': label, 'argv': argv, 'exitCode': code, 'seconds': time.monotonic() - start})
        product.write(directory / 'original-exit.json', {'exitCode': code})
        product.write(output / 'context.json', context)
        if code: raise ValueError('Original diagnostic failed: ' + label)
    try:
        if not subprocess.check_output([str(tool), '--version'], text=True).strip().startswith('18.12.0+'):
            raise ValueError('Unpinned collector')
        for mode in modes or ('sqlite-strict', 'pg-natural', 'pg-gated'):
            directory = output / mode
            directory.mkdir()
            env = owners.environment(directory)
            argv = [str(tool), 'collect', '--settings', str(settings), '--output-format', 'cobertura', '--output', str(directory / 'original.xml'), *command(mode, directory)]
            run(mode, argv, directory, env)
            observed = json.loads((directory / 'observations-original.json').read_text())
            if len(observed['observations']) != 32 or any(row['flags'] != 1 for row in observed['observations']):
                raise ValueError('Missing/incorrect original product observations')
            context['results'].append({'mode': mode, 'targetRows': target_rows(directory / 'original.xml'), 'observations': observed['observations'], 'il': observed['il']})
            product.write(output / 'context.json', context)
        context['after'] = snapshot()
        context['inputsAfter'] = input_bindings([Path(path) for path in inputs])
        context['materialBindingsUnchanged'] = context['before'] == context['after'] and context['inputsBefore'] == context['inputsAfter']
        if not context['materialBindingsUnchanged']: raise ValueError('Canonical product/fixture/SDK/runtime/tool changed')
    except BaseException as error:
        context['error'] = {'type': type(error).__name__, 'message': str(error)}
    finally:
        try: context['cleanup'] = owner.cleanup()
        except BaseException as error: context['cleanup'] = {'complete': False, 'errorType': type(error).__name__, 'message': str(error)}
        context['originalFiles'] = {str(path.relative_to(output)): digest(path) for path in output.rglob('*') if path.is_file() and path.name != 'context.json'
                                    and not any(part in ('nuget', 'cli', 'msbuild') for part in path.relative_to(output).parts)}
        product.write(output / 'context.json', context)
    print(output / 'context.json')
    return 1 if context.get('error') or not context['cleanup']['complete'] else 0


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--mode', choices=('replay', 'focus', 'diagnostic'), required=True)
    parser.add_argument('--baseline', type=Path)
    parser.add_argument('--candidate', type=Path)
    parser.add_argument('--tool', type=Path)
    parser.add_argument('--build-root', type=Path, default=product.cost.TEST_OUTPUT)
    parser.add_argument('--fixture-root', type=Path)
    parser.add_argument('--diagnostic-mode', action='append', choices=('sqlite-strict', 'pg-natural', 'pg-gated'))
    parser.add_argument('--call-order', choices=('role-first', 'user-first'), default='user-first')
    parser.add_argument('--output', type=Path, required=True)
    args = parser.parse_args()
    def interrupted(*_): raise KeyboardInterrupt('Original invocation interrupted')
    signal.signal(signal.SIGTERM, interrupted)
    output = args.output.resolve()
    output.mkdir(parents=True, exist_ok=False)
    if args.mode == 'diagnostic':
        if not args.tool or not args.fixture_root: parser.error('Diagnostic requires --tool and --fixture-root')
        return diagnostic(output, args.tool, args.build_root, args.fixture_root, args.diagnostic_mode, args.call_order)
    if args.mode == 'focus':
        if not args.tool:
            parser.error('Focus requires pinned --tool')
        return focus(output, args.tool, args.build_root)
    if not args.baseline or not args.candidate:
        parser.error('Replay requires --baseline and --candidate')
    baseline, candidate = args.baseline.resolve(), args.candidate.resolve()
    before = {str(p): digest(p) for p in (baseline, candidate)}
    result = {'schemaVersion': 1, 'scope': 'Readonly original coverage-observation replay; no cause inference',
              'originalReportHashesBefore': before, **compare(baseline, candidate)}
    result['originalReportHashesAfter'] = {str(p): digest(p) for p in (baseline, candidate)}
    if before != result['originalReportHashesAfter']:
        raise ValueError('Original reports changed')
    (output / 'result.json').write_text(json.dumps(result, indent=2) + '\n')
    print(json.dumps({k: result[k] for k in ('modelEqual', 'observationsEqual', 'countsEqual', 'baselineOnlyRows', 'candidateOnlyRows')}, indent=2))
    return 0 if result['observationsEqual'] else 1


if __name__ == '__main__':
    if len(sys.argv) == 3 and sys.argv[1] == '--children':
        raise SystemExit(child(sys.argv[2]))
    raise SystemExit(main())
