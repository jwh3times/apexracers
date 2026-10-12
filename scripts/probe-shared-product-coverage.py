#!/usr/bin/env python3
"""Compare one shared collection with a retained immutable full-suite MTP control.

The control may belong to an experiment whose later candidate failed. Only its
original successful baseline is used; every failed candidate remains evidence.
No suites are retried, and this tool does not select a production topology.
"""
import argparse
from collections import Counter
import importlib.util
import json
import os
from pathlib import Path
import shutil
import signal
import subprocess
import time
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[1]
spec = importlib.util.spec_from_file_location('product', ROOT / 'scripts/probe-product-coverage-preservation.py')
product = importlib.util.module_from_spec(spec)
spec.loader.exec_module(product)


def map_discovery(canonical, actual):
    if len({r['uid'] for r in actual}) != len(actual) or len({r['displayName'] for r in actual}) != len(actual):
        raise ValueError('Ambiguous copied discovery')
    expected = {r['displayName']: {k: v for k, v in r.items() if k != 'uid'} for r in canonical}
    found = {r['displayName']: {k: v for k, v in r.items() if k != 'uid'} for r in actual}
    if expected != found or len(canonical) != len(actual):
        raise ValueError('Copied discovery changed cases or other metadata')
    return {r['displayName']: r['uid'] for r in actual}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--baseline-context', type=Path, required=True)
    parser.add_argument('--tool', type=Path, required=True)
    parser.add_argument('--output', type=Path, required=True)
    args = parser.parse_args()
    reference = args.baseline_context.resolve()
    control = json.loads(reference.read_text())
    baseline = reference.parent / 'baseline'
    baseline_command = [r for r in control['commands'] if r['label'] == 'baseline']
    if len(baseline_command) != 1 or baseline_command[0]['exitCode'] != 0:
        raise ValueError('No original successful full-suite control')
    rows = json.loads((reference.parent / 'discovery-original.json').read_text())['tests']
    if product.cost.read_results(baseline / 'original.trx') != (Counter(r['displayName'] for r in rows), Counter({'Passed': len(rows)})):
        raise ValueError('Control discovery/results are incomplete or non-passing')
    product.coverage_gate(baseline / 'original.xml')
    tool = args.tool.resolve()
    store = tool.parent / '.store/dotnet-coverage/18.12.0'
    runtimes = Path(shutil.which('dotnet')).resolve().parent / 'shared'
    if (product.cost.inventory() != control['sourceBuildBefore']
        or product.cost.sdk_inventory() != control['sdkBefore']
        or product.probe.inventory(store) != control['toolBefore']
        or product.probe.inventory(runtimes) != control['runtimeBefore']):
        raise ValueError('Product build/source/SDK/tool/runtime differs from control')
    if not subprocess.check_output([str(tool), '--version'], text=True).strip().startswith('18.12.0+'):
        raise ValueError('Unpinned collector')
    out = args.output.resolve()
    out.mkdir(parents=True, exist_ok=False)
    material = [Path(__file__).resolve(), ROOT / 'scripts/probe-product-coverage-preservation.py',
                ROOT / 'scripts/probe-coverage-preservation.py', ROOT / 'scripts/measure-backend-cost.py', ROOT / 'ApexRacers.sln']
    inputs = {str(p.relative_to(ROOT)): product.cost.digest(p) for p in material}
    originals = [reference, baseline / 'original.xml', baseline / 'original.trx', baseline / 'original.log',
                 baseline / 'command-original.json', reference.parent / 'discovery-original.json', reference.parent / 'partition.json']
    original_hashes = {str(p): product.cost.digest(p) for p in originals}
    context = {'schemaVersion': 1, 'compiledSourceHead': control['sourceHead'], 'referenceControl': str(reference),
               'referenceControlFilesBefore': original_hashes, 'researchInputsBefore': inputs, 'commands': [],
               'liveEligible': False, 'adoptionEligible': False,
               'scope': 'One local shared-session preservation comparison; not hosted timing/cancellation acceptance'}
    product.write(out / 'context.json', context)
    shutil.copy2(__file__, out / 'runner-original.py')
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
    shards = json.loads((reference.parent / 'partition.json').read_text())['shards']
    if len(shards) != 2 or Counter(r['uid'] for shard in shards for r in shard) != Counter(r['uid'] for r in rows):
        raise ValueError('Partition is not complete and disjoint')
    canonical_by_uid = {r['uid']: r for r in rows}
    if any(r != canonical_by_uid[r['uid']] for shard in shards for r in shard):
        raise ValueError('Partition row differs from original canonical discovery')
    owners = {}
    for index, shard in enumerate(shards):
        for r in shard:
            cls = (r['type']['namespace'], r['type']['typeName'])
            if cls in owners and owners[cls] != index:
                raise ValueError('Whole class split across producers')
            owners[cls] = index
    children = []
    for index, shard in enumerate(shards, 1):
        directory = shared / f'shard-{index}'
        directory.mkdir()
        copy = directory / 'build'
        shutil.copytree(product.cost.TEST_OUTPUT, copy)
        if product.probe.inventory(copy) != product.probe.inventory(product.cost.TEST_OUTPUT):
            raise ValueError('Copied build bytes differ')
        command = [str(copy / 'ApexRacers.Tests'), '--list-tests', 'json']
        result = subprocess.run(command, cwd=ROOT, capture_output=True, text=True)
        (directory / 'discovery-original.json').write_text(result.stdout)
        (directory / 'discovery-original.stderr').write_text(result.stderr)
        context['commands'].append({'label': f'discovery-{index}', 'argv': command, 'exitCode': result.returncode})
        product.write(out / 'context.json', context)
        if result.returncode:
            raise ValueError('Original copied discovery failed')
        mapping = map_discovery(rows, json.loads(result.stdout)['tests'])
        product.write(directory / 'uid-mapping-original.json',
                      [{'canonicalUid': r['uid'], 'copiedUid': mapping[r['displayName']], 'displayName': r['displayName']} for r in shard])
        children.append({'directory': str(directory), 'executable': str(copy / 'ApexRacers.Tests'),
                         'uids': [mapping[r['displayName']] for r in shard]})
    product.write(shared / 'children-original.json', children)
    command = [str(tool), 'collect', '--settings', str(shared / 'settings-original.config'), '--output-format', 'cobertura',
               '--output', str(shared / 'original.xml'), 'python3', str(ROOT / 'scripts/probe-product-coverage-preservation.py'),
               '--child', str(shared / 'children-original.json')]
    product.write(shared / 'command-original.json', command)
    containers = product.docker_ids()
    start = time.monotonic()
    with (shared / 'original.log').open('w') as log:
        process = subprocess.Popen(command, cwd=ROOT, stdout=log, stderr=subprocess.STDOUT, start_new_session=True)
        try:
            code = process.wait()
        except BaseException:
            os.killpg(process.pid, signal.SIGTERM)
            try:
                process.wait(timeout=30)
            except subprocess.TimeoutExpired:
                os.killpg(process.pid, signal.SIGKILL)
                process.wait()
            context['interruptedExitCode'] = process.returncode
            product.write(out / 'context.json', context)
            raise
    context['commands'].append({'label': 'shared', 'argv': command, 'exitCode': code, 'seconds': time.monotonic() - start})
    product.write(out / 'context.json', context)
    if code:
        raise ValueError('Original shared collection failed; no replacement')
    for index, shard in enumerate(shards, 1):
        directory = shared / f'shard-{index}'
        if product.cost.read_results(directory / 'original.trx') != (Counter(r['displayName'] for r in shard), Counter({'Passed': len(shard)})):
            raise ValueError('Original producer inventory/outcomes differ')
        if product.probe.inventory(directory / 'build') != product.probe.inventory(product.cost.TEST_OUTPUT):
            raise ValueError('Copied build changed')
    context['baselineCoverage'] = product.coverage_gate(baseline / 'original.xml')
    context['sharedCoverage'] = product.coverage_gate(shared / 'original.xml')
    context['modelEqual'] = product.normalized_report(baseline / 'original.xml') == product.normalized_report(shared / 'original.xml')
    context['observationsEqual'] = product.normalized_report(baseline / 'original.xml', True) == product.normalized_report(shared / 'original.xml', True)
    context['countsEqual'] = all(context['baselineCoverage'][k] == context['sharedCoverage'][k]
                                 for k in ('lines-valid', 'branches-valid', 'lines-covered', 'branches-covered'))
    context['sourceBuildUnchanged'] = product.cost.inventory() == control['sourceBuildBefore']
    context['sdkUnchanged'] = product.cost.sdk_inventory() == control['sdkBefore']
    context['toolUnchanged'] = product.probe.inventory(store) == control['toolBefore']
    context['runtimeUnchanged'] = product.probe.inventory(runtimes) == control['runtimeBefore']
    context['researchInputsUnchanged'] = {str(p.relative_to(ROOT)): product.cost.digest(p) for p in material} == inputs
    context['referenceControlUnchanged'] = {str(p): product.cost.digest(p) for p in originals} == original_hashes
    context['immediateAdditionalContainers'] = sorted(set(product.docker_ids()) - set(containers))
    # Resource disposal may finish shortly after the managed processes exit.
    # Retain the immediate snapshot and fail closed if the observed delta persists.
    cleanup_started = time.monotonic()
    context['additionalContainers'] = list(context['immediateAdditionalContainers'])
    while context['additionalContainers'] and time.monotonic() - cleanup_started < 10:
        time.sleep(.25)
        context['additionalContainers'] = sorted(set(product.docker_ids()) - set(containers))
    context['cleanupObservationSeconds'] = time.monotonic() - cleanup_started
    context['preservationDemonstrated'] = all(context[k] for k in ('modelEqual', 'observationsEqual', 'countsEqual',
        'sourceBuildUnchanged', 'sdkUnchanged', 'toolUnchanged', 'runtimeUnchanged', 'researchInputsUnchanged', 'referenceControlUnchanged')) and not context['additionalContainers']
    context['originalArtifacts'] = {str(p.relative_to(out)): product.cost.digest(p) for p in out.rglob('*') if p.is_file() and p.name != 'context.json'}
    product.write(out / 'context.json', context)
    if not context['preservationDemonstrated']:
        raise ValueError('Preservation comparison failed; originals retained')
    print(out / 'context.json')


if __name__ == '__main__':
    def cancelled(*_):
        raise KeyboardInterrupt()
    signal.signal(signal.SIGTERM, cancelled)
    main()
