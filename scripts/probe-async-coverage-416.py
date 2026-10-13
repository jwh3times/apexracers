#!/usr/bin/env python3
"""Bounded compiler-cache diagnosis; preserve each original collection and export."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import signal
import shutil
import subprocess
import time
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[1]


def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def inventory(path):
    return {str(p.relative_to(path)): sha(p) for p in sorted(path.rglob('*')) if p.is_file() and not p.is_symlink()}


def write(path, value):
    path.write_text(json.dumps(value, indent=2) + '\n')


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--tool', type=Path)
    parser.add_argument('--verify-real-pair', nargs=2, type=Path, metavar=('FIRST_CONTEXT', 'SECOND_CONTEXT'))
    parser.add_argument('--output', type=Path)
    parser.add_argument('--real-cache-warm', action='store_true')
    parser.add_argument('--canonical', type=Path)
    parser.add_argument('--fixture-root', type=Path)
    parser.add_argument('--settings', type=Path)
    parser.add_argument('--collection-format', choices=('coverage', 'cobertura'), default='coverage')
    parser.add_argument('--build-root', type=Path, default=ROOT / 'scripts/async-coverage-minimal/bin/Release/net10.0')
    args = parser.parse_args()
    if args.verify_real_pair:
        a, b = [json.loads(p.read_text()) for p in args.verify_real_pair]
        if not all(d['materialBindingsUnchanged'] for d in (a, b)) or a['before'] != b['before']:
            raise ValueError('Real pair byte bindings differ')
        equal = a['results'][0]['targetRows'] == b['results'][0]['targetRows']
        print(json.dumps({'sameBinary': True, 'targetObservationsEqual': equal}))
        if not equal:
            raise SystemExit(1)
        return
    if args.tool is None or args.output is None:
        parser.error('--tool and --output are required for collection')
    output = args.output.resolve()
    output.mkdir(parents=True, exist_ok=False)
    tool, build = args.tool.resolve(), args.build_root.resolve()
    version = subprocess.check_output([str(tool), '--version'], text=True).strip()
    sdk = subprocess.check_output(['dotnet', '--version'], text=True).strip()
    if not version.startswith('18.12.0+') or sdk != '10.0.112':
        raise ValueError('SDK/tool pins differ')
    runtime_root = Path(subprocess.check_output(['readlink', '-f', subprocess.check_output(['which', 'dotnet'], text=True).strip()], text=True).strip()).parent
    settings = output / 'settings-original.config'
    settings.write_text('''<?xml version="1.0" encoding="utf-8"?>
<Configuration><CodeCoverage><ModulePaths><Include><ModulePath>.*AsyncCoverageMinimal.*</ModulePath></Include></ModulePaths><UseVerifiableInstrumentation>False</UseVerifiableInstrumentation><EnableStaticManagedInstrumentation>False</EnableStaticManagedInstrumentation><EnableDynamicManagedInstrumentation>True</EnableDynamicManagedInstrumentation><EnableStaticNativeInstrumentation>False</EnableStaticNativeInstrumentation><EnableDynamicNativeInstrumentation>False</EnableDynamicNativeInstrumentation><CollectFromChildProcesses>True</CollectFromChildProcesses></CodeCoverage></Configuration>
''')
    if args.real_cache_warm:
        if not all((args.canonical, args.fixture_root, args.settings)):
            parser.error('Real warming requires --canonical --fixture-root --settings')
        shutil.copy2(args.settings, settings)
    sources = [Path(__file__).resolve(), ROOT / 'scripts/async-coverage-minimal/Program.cs', ROOT / 'scripts/async-coverage-minimal/Minimal.csproj', ROOT / 'scripts/async-coverage-minimal/RealWarm.cs', ROOT / 'scripts/async-coverage-minimal/RealWarm.csproj', ROOT / 'global.json', ROOT / 'Directory.Build.props', ROOT / 'Directory.Packages.props', settings]
    if args.real_cache_warm:
        sources.extend([ROOT / 'scripts/async-coverage-probe/Program.cs', ROOT / 'scripts/async-coverage-probe/Probe.csproj'])
        for path in args.canonical.resolve().glob('*'):
            if path.suffix in ('.dll', '.pdb') and (not (args.fixture_root.resolve() / path.name).is_file() or sha(path) != sha(args.fixture_root.resolve() / path.name)):
                raise ValueError('Diagnostic copied canonical dependency differs: ' + path.name)
    def capture():
        additional = {'canonical': inventory(args.canonical.resolve()), 'diagnostic': inventory(args.fixture_root.resolve())} if args.real_cache_warm else {}
        return {'additional': additional, 'inputs': {str(p): sha(p) for p in sources if p.exists()}, 'build': inventory(build), 'tool': inventory(tool.parent / '.store/dotnet-coverage/18.12.0'), 'sdk': inventory(runtime_root / 'sdk/10.0.112'), 'runtime': {'sharedFrameworks': inventory(runtime_root / 'shared'), 'host': inventory(runtime_root / 'host'), 'dotnetHostSha256': sha(runtime_root / 'dotnet')}}
    for index, path in enumerate(sources):
        if path.exists(): shutil.copy2(path, output / f'input-original-{index}-{path.name}')
    context = {'schemaVersion': 1, 'scope': ('Actual unchanged AdminService with research-only cache prewarm' if args.real_cache_warm else 'Independent package-free compiler pattern; no production denominator or source change'), 'collectionFormat': 'cobertura' if args.real_cache_warm else args.collection_format, 'sdk': sdk, 'tool': version, 'commands': [], 'results': [], 'before': capture(), 'adoptionEligible': False, 'liveEligible': False}
    def run(label, command):
        directory = output / label
        directory.mkdir(exist_ok=True)
        write(directory / 'command-original.json', command)
        start = time.monotonic()
        with (directory / 'original.log').open('w') as log:
            process = subprocess.Popen(command, stdout=log, stderr=subprocess.STDOUT, start_new_session=True, cwd=ROOT)
            try:
                code = process.wait(timeout=30)
            except subprocess.TimeoutExpired:
                os.killpg(process.pid, signal.SIGTERM)
                try:
                    process.wait(timeout=3)
                except subprocess.TimeoutExpired:
                    os.killpg(process.pid, signal.SIGKILL)
                    process.wait(timeout=3)
                context['commands'].append({'label': label, 'argv': command, 'exitCode': process.returncode, 'timedOut': True, 'seconds': time.monotonic() - start})
                raise
        context['commands'].append({'label': label, 'argv': command, 'exitCode': code, 'seconds': time.monotonic() - start})
        write(output / 'context.json', context)
        if code:
            raise ValueError(f'Original failed command {label}: {code}')
    try:
        if args.real_cache_warm:
            for order in ('role-first', 'user-first'):
                label = 'real-warm-' + order
                directory = output / label
                directory.mkdir()
                run(label, [str(tool), 'collect', '--settings', str(settings), '--output-format', 'cobertura', '--output', str(directory / 'original.cobertura.xml'), 'dotnet', 'exec', '--depsfile', str(args.canonical.resolve() / 'ApexRacers.Tests.deps.json'), '--runtimeconfig', str(args.canonical.resolve() / 'ApexRacers.Tests.runtimeconfig.json'), str(build / 'RealCacheWarm.dll'), str(args.fixture_root.resolve() / 'AsyncCoverageDiagnostic.dll'), order, str(directory / 'observations-original.json'), str(directory / 'warm-original.json')])
                observations = json.loads((directory / 'observations-original.json').read_text())
                warm = json.loads((directory / 'warm-original.json').read_text())
                report = ET.parse(directory / 'original.cobertura.xml').getroot()
                rows = [{'class': cls.attrib['name'], 'method': method.attrib['name'], 'line': line.attrib}
                        for cls in report.findall('.//class') for method in cls.findall('./methods/method') for line in method.findall('./lines/line') if line.attrib.get('branch') == 'True' and cls.attrib['name'].startswith('ApexRacers.Api.Services.AdminService.<GetFlagsFor')]
                context['results'].append({'mode': 'real-cache-warm', 'order': order, 'warm': warm, 'observations': observations, 'branchRows': rows})
                if len(observations['observations']) != 32 or not all(o['completedOnReturn'] and o['flags'] == 1 and o['totalPosts'] == 0 for o in observations['observations']):
                    raise ValueError('Unexpected real SQLite observations')
                if len(rows) != 2 or any(r['line']['condition-coverage'] != '50% (1/2)' for r in rows):
                    raise ValueError('Real prewarm prediction failed')
        else:
            minimal_controls(output, tool, build, settings, args, context, run)
    except BaseException as error:
        context['error'] = repr(error)
        raise
    finally:
        context['after'] = capture()
        context['bindingsUnchanged'] = context['before'] == context['after']
        context['originalFiles'] = {str(p.relative_to(output)): sha(p) for p in sorted(output.rglob('*')) if p.is_file() and p.name != 'context.json'}
        write(output / 'context.json', context)
    if not context['bindingsUnchanged']:
        raise ValueError('Material inputs changed')
    print(output / 'context.json')


def minimal_controls(output, tool, build, settings, args, context, run):
    run('uninstrumented', ['dotnet', str(build / 'AsyncCoverageMinimal.dll'), 'inspect', 'role-first', str(output / 'uninstrumented/il-pdb-original.json')])
    for mode in ('cold', 'warm', 'fresh'):
        for order in ('role-first', 'user-first'):
            label = mode + '-' + order
            directory = output / label
            directory.mkdir()
            native = directory / ('original.coverage' if args.collection_format == 'coverage' else 'original.cobertura.xml')
            run(label, [str(tool), 'collect', '--settings', str(settings), '--output-format', args.collection_format, '--output', str(native), 'dotnet', str(build / 'AsyncCoverageMinimal.dll'), mode, order, str(directory / 'observations-original.json')])
            native_hash = sha(native)
            for format in (('xml', 'cobertura') if args.collection_format == 'coverage' else ()):
                run(label + '-export-' + format, [str(tool), 'merge', str(native), '--output-format', format, '--output', str(directory / ('original.' + format + '.xml'))])
                if sha(native) != native_hash:
                    raise ValueError('Native input changed during export')
            observations = json.loads((directory / 'observations-original.json').read_text())
            if observations['runtime'] != '10.0.12' or len(observations['observations']) != 32 or not all(o['completedOnReturn'] for o in observations['observations']):
                raise ValueError('Unexpected runtime/wrapper observations')
            report = ET.parse(directory / 'original.cobertura.xml').getroot()
            rows = [{'class': cls.attrib['name'], 'method': method.attrib['name'], 'line': line.attrib}
                    for cls in report.findall('.//class') for method in cls.findall('./methods/method') for line in method.findall('./lines/line') if line.attrib.get('branch') == 'True']
            context['results'].append({'mode': mode, 'order': order, 'collectedSha256': native_hash, 'branchRows': rows})
            write(output / 'context.json', context)

if __name__ == '__main__':
    main()
