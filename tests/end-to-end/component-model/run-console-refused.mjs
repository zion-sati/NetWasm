// Runs a command that writes 2,000 lines under NetWasm's JavaScript host with a sink that refuses
// output after 1 KiB, as a page that caps its console does. The host stops taking the output and
// the program sees a closed pipe, which .NET's console ignores: the run completes. Left open, the
// stream kept the program waiting for a reader that never came back, until jco trapped.
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
const limit = 1024;
let accepted = 0;
let refused = 0;
const sink = Object.freeze({ write(bytes) {
  if (accepted + bytes.byteLength > limit) {
    refused++;
    throw new Error('console limit exceeded');
  }
  accepted += bytes.byteLength;
} });
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
if (outcome.completionKind !== 'normal' || outcome.exitCode !== 0 || refused !== 1 || accepted > limit) {
  console.error(`run completed ${JSON.stringify(outcome)} after the sink accepted ${accepted} bytes and refused ${refused} writes`);
  process.exit(1);
}
process.stdout.write(`completed after ${accepted} bytes\n`);
