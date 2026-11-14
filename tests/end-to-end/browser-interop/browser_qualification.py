#!/usr/bin/env python3
"""Publish the browser fixtures from a NetWasm package folder and run each in
Chromium, Firefox and WebKit.

Each fixture is copied into its own cell, restored from the package folder and
nuget.org, published, and driven by run-browser.mjs. A fixture may list build
variants in variants.json; each variant is a separate cell.
"""

import argparse
import hashlib
import json
import os
from pathlib import Path
import shutil
import subprocess
from xml.sax.saxutils import escape

FIXTURES = Path(__file__).resolve().parent
KINDS = ['command', 'worker', 'component', 'wit', 'page', 'hosts', 'timezone',
         'issue89-method-group', 'issue89-finalization', 'issue89-static-roots']
TOOL_ENVIRONMENT = ['EMSDK', 'EMSDK_ROOT', 'EMSDK_NODE', 'NETWASM_EMSDK_ROOT', 'NETWASM_NODE_MODULES_ROOT',
                    'NETWASM_NODE_PATH', 'NETWASM_WASM_LD_PATH', 'NETWASM_WASM_OPT_PATH', 'NETWASM_WASM_MERGE_PATH']


def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def consumer_environment(root):
    """An isolated package cache and CLI home, with no maintainer tool overrides."""
    env = dict(os.environ, NUGET_PACKAGES=str(root / 'packages'), DOTNET_CLI_HOME=str(root / 'dotnet-home'),
               DOTNET_CLI_TELEMETRY_OPTOUT='1', DOTNET_CLI_UI_LANGUAGE='en-US', MSBUILDDISABLENODEREUSE='1')
    for key in TOOL_ENVIRONMENT:
        env.pop(key, None)
    return env


def variants(kind):
    # A fixture may build several variants, each a set of MSBuild properties.
    path = FIXTURES / kind / 'variants.json'
    return json.loads(path.read_text()).items() if path.exists() else [(None, {})]


def run_cells(*, root, env, feed, version, sdk_version, node, kinds, profiles, collectors, summary, save,
              component_optimization=None, verify=None):
    """Publishes and drives every kind, variant, collector and profile cell.

    verify, when given, is called as verify(cell, run, stage) before publishing
    ("verify-before") and after the browser run ("verify-after").
    """
    if 'wit' in kinds:
        (root / 'global.json').write_text(json.dumps({'sdk': {'version': sdk_version, 'rollForward': 'disable'}}))
        with (root / 'template-install.stdout').open('x') as out, (root / 'template-install.stderr').open('x') as err:
            code = subprocess.run(['dotnet', 'new', 'install', str(feed / f'NetWasm.Templates.{version}.nupkg'),
                                   '--force'], cwd=root, env=env, stdout=out, stderr=err).returncode
        summary['templateInstall'] = code
        save()
        assert code == 0

    for kind, collector in [(k, c) for c in collectors for k in kinds]:
        for (variant, properties), profile in [(v, p) for v in variants(kind) for p in profiles]:
            prefix = f'{collector.lower()}-' if collector else ''
            suffix = f'-{variant}' if variant else ''
            cell = root / f'{prefix}{kind}{suffix}-{profile.lower()}'
            selection = ['-p:NetWasmGarbageCollector=' + collector] if collector else []
            selection += [f'-p:{name}={value}' for name, value in properties.items()]
            if kind == 'wit':
                cell.mkdir()
                with (cell / 'template-create.stdout').open('x') as out, \
                        (cell / 'template-create.stderr').open('x') as err:
                    code = subprocess.run(['dotnet', 'new', 'netwasm-app', '-n', 'App', '-o', str(cell),
                                           '--worker', 'wit'], cwd=root, env=env, stdout=out, stderr=err).returncode
                assert code == 0
                for name in ['Program.cs', 'app.mjs']:
                    shutil.copyfile(FIXTURES / kind / name, cell / name)
            else:
                shutil.copytree(FIXTURES / kind, cell)
            (cell / 'global.json').write_text(json.dumps({
                'sdk': {'version': sdk_version, 'rollForward': 'disable'},
                'msbuild-sdks': {'NetWasm.Sdk': version}}))
            (cell / 'NuGet.Config').write_text('<configuration><packageSources><clear/>'
                f'<add key="candidate" value="{escape(str(feed), {chr(34): "&quot;"})}"/>'
                '<add key="nuget.org" value="https://api.nuget.org/v3/index.json"/>'
                '</packageSources></configuration>')
            inputs = {str(p.relative_to(cell)): digest(p) for p in cell.rglob('*') if p.is_file()}
            row = {'kind': kind, 'variant': variant, 'profile': profile, 'collector': collector,
                   'inputs': inputs, 'stages': {}}
            summary['cells'].append(row)

            def run(name, command, cell=cell, row=row, kind=kind, profile=profile):
                with (cell / (name + '.stdout')).open('x') as out, (cell / (name + '.stderr')).open('x') as err:
                    code = subprocess.run(command, cwd=cell, env=env, stdout=out, stderr=err).returncode
                row['stages'][name] = code
                save()
                if code != 0:
                    # The stage's own output, since the uploaded results carry only stderr and dotnet reports
                    # build errors on stdout.
                    for stream in ('stdout', 'stderr'):
                        text = (cell / f'{name}.{stream}').read_text(errors='replace').splitlines()
                        print(f'--- {kind}/{profile} {name} {stream} (last 80 lines)', *text[-80:], sep='\n')
                assert code == 0, f'{kind}/{profile}: {name} failed'

            run('restore', ['dotnet', 'restore', 'App.csproj', '--configfile', 'NuGet.Config', *selection])
            if verify is not None:
                verify(cell, run, 'verify-before')
            publish = ['dotnet', 'publish', 'App.csproj', '-c', profile, '--no-restore', '--disable-build-servers',
                       *selection]
            if kind == 'component' and component_optimization:
                row['optimization'] = component_optimization
                publish.append('-p:NetWasmOptimization=' + component_optimization)
            run('publish', publish)
            published = cell / f'bin/{profile}/netwasm0.1/publish'

            def output_hashes(cell=cell, kind=kind, published=published):
                # Component mode executes build output through dotnet run,
                # whereas browser modes serve the published deployment.
                artifacts = cell / 'bin' if kind == 'component' else published
                # dotnet run writes <AssemblyName>.netwasm.{request,result,execution}.json.
                run_records = ('.netwasm.request.json', '.netwasm.result.json', '.netwasm.execution.json')
                return {str(p.relative_to(cell)): digest(p) for p in artifacts.rglob('*')
                        if p.is_file() and not p.name.endswith(run_records)}

            built_outputs = output_hashes()
            row['builtOutputs'] = built_outputs
            metadata_args = ['dotnet', 'msbuild', 'App.csproj', '-t:Build', '-p:Configuration=' + profile,
                             '-getItem:NetWasmComponentRuntime', '-nologo', '-v:quiet', *selection]
            if kind == 'component' and component_optimization:
                metadata_args.append('-p:NetWasmOptimization=' + component_optimization)
            run('runtime-metadata', metadata_args)
            text = (cell / 'runtime-metadata.stdout').read_text()
            start = text.rfind('\n{')
            metadata = json.loads(text[start + 1:] if start >= 0 else text)
            modules = metadata['Items']['NetWasmComponentRuntime']
            assert len(modules) == 1
            runtime = modules[0]
            assert runtime['WasmTarget'] == 'wasm32'
            assert collector is None or runtime['GarbageCollector'] == collector
            assert digest(Path(runtime['Identity'])) == runtime['Digest']
            assert output_hashes() == built_outputs, 'Metadata build changed published artifacts'
            row['resolvedRuntime'] = {'collector': runtime['GarbageCollector'], 'target': runtime['WasmTarget'],
                                      'sha256': runtime['Digest']}
            if kind == 'component':
                run('execute', ['dotnet', 'run', '--project', 'App.csproj', '-c', profile,
                                '--no-build', '--no-restore', *selection])
                row['execution'] = {
                    'stdout42': (cell / 'execute.stdout').read_text().strip() == '42',
                    'stderrEmpty': not (cell / 'execute.stderr').read_bytes(),
                }
                behavior_passed = all(row['execution'].values())
            else:
                run('browser', [node, str(FIXTURES / 'run-browser.mjs'), str(published), kind,
                                str(cell / 'browser-result.json'), profile])
                row['browser'] = json.loads((cell / 'browser-result.json').read_text())
                behavior_passed = row['browser']['passed']
            if verify is not None:
                verify(cell, run, 'verify-after')
            row['inputsUnchanged'] = all(digest(cell / p) == h for p, h in inputs.items())
            row['outputs'] = output_hashes()
            row['outputsUnchanged'] = row['outputs'] == built_outputs
            row['passed'] = behavior_passed and row['inputsUnchanged'] and row['outputsUnchanged']
            save()
            assert row['passed']
    summary['passed'] = all(row['passed'] for row in summary['cells'])
    save()


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--packages', type=Path, required=True, help='folder holding the NetWasm packages')
    parser.add_argument('--version', required=True, help='NetWasm package version to consume')
    parser.add_argument('--output', type=Path, required=True)
    parser.add_argument('--node', default=shutil.which('node'))
    parser.add_argument('--kinds', nargs='+', choices=KINDS, default=[k for k in KINDS if k != 'component'])
    parser.add_argument('--profiles', nargs='+', choices=['Debug', 'Release'], default=['Release'])
    args = parser.parse_args()
    root = args.output.resolve()
    root.mkdir(parents=True, exist_ok=False)
    env = consumer_environment(root)
    sdk_version = subprocess.run(['dotnet', '--version'], cwd=root, env=env, check=True,
                                 capture_output=True, text=True).stdout.strip()
    summary = {'candidate': args.version, 'cells': []}

    def save():
        (root / 'summary.json').write_text(json.dumps(summary, indent=2))

    run_cells(root=root, env=env, feed=args.packages.resolve(), version=args.version, sdk_version=sdk_version,
              node=args.node, kinds=args.kinds, profiles=args.profiles, collectors=[None], summary=summary,
              save=save)


if __name__ == '__main__':
    main()
