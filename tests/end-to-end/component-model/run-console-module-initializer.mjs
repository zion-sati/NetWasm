// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

// Runs a command whose module initializer writes to the console under NetWasm's JavaScript host.
// jco traps any wait while it instantiates a component ("cannot block a synchronous task before
// returning"), so the initializer must run in wasi:cli/run, once, before Main.
import { readFile } from 'node:fs/promises';
import { pathToFileURL } from 'node:url';

const [generatedRoot] = process.argv.slice(2);
if (!generatedRoot) throw new Error('generated component directory is required');
const { commandComponentContract } = await import('../../../src/NetWasm.Hosting/JavaScript/canonical-component-binder.mjs');
const { executeComponent } = await import('../../../src/NetWasm.Hosting/JavaScript/component-executor.mjs');
const { createWasiPlatform } = await import('../../../src/NetWasm.Hosting/JavaScript/wasi-platform.mjs');
const base = pathToFileURL(`${generatedRoot.replace(/\/$/u, '')}/`);
const generated = await import(new URL('component.js', base));
const loadCoreModule = file => readFile(new URL(file, base)).then(WebAssembly.compile);
const decoder = new TextDecoder();
let output = '';
const sink = Object.freeze({ write: bytes => { output += decoder.decode(bytes, { stream: true }); } });
const platform = createWasiPlatform(Object.freeze({ initialDirectory: null, network: null }))(Object.freeze({
  arguments: Object.freeze([]),
  environment: Object.freeze([]),
  filesystem: null,
  grants: Object.freeze({ network: 'denyAll' }),
  signal: null,
  stderr: sink,
  stdin: null,
  stdout: sink,
}));
const imports = {};
for (const [name, value] of Object.entries(platform.imports)) {
  imports[name] = value;
  imports[name.replace(/@[0-9.]+$/u, '')] = value;
}
const adapter = Object.freeze({
  contractKey: commandComponentContract,
  async instantiate(request) {
    const root = await generated.instantiate(request.loadCoreModule, request.imports, request.instantiateCore);
    const command = root['wasi:cli/run@0.3.2'];
    return Object.freeze({ command: Object.freeze({ async run() { await command.run(); return 0; } }) });
  },
});
const outcome = await executeComponent({
  contractKey: commandComponentContract, adapter, imports, loadCoreModule,
  releaseActions: [Object.freeze({ phase: 'output', code: 'host.platform-output',
    message: 'The execution host could not deliver application output.', release: () => platform.release() })],
});
if (outcome.completionKind !== 'normal' || output !== 'initializer\nmain 1\n') {
  console.error(`run completed ${outcome.completionKind}: ${JSON.stringify(outcome.failure?.detail ?? outcome)} output ${JSON.stringify(output)}`);
  process.exit(1);
}
process.stdout.write(output);
