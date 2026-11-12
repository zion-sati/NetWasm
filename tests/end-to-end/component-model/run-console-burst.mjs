// Runs a command that writes 2,000 lines and returns at once under NetWasm's JavaScript host,
// whose reads of standard output lag behind the program and which releases the platform (dropping
// any stream still open) once the run completes, as the browser worker does. Every line must have
// reached the sink by then: the program's exit waits until its output is written (issue 224).
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
// A host that passes output on through a slower channel: each read of the guest's stream waits
// longer than the release waits for the drains.
const lagging = stream => Object.freeze({
  read: async options => {
    await new Promise(resolve => setTimeout(resolve, 25));
    return stream.read(options);
  },
});
const imports = {};
for (const [name, value] of Object.entries(platform.imports)) {
  const module = name.startsWith('wasi:cli/stdout@')
    ? Object.freeze({ writeViaStream: stream => value.writeViaStream(lagging(stream)) })
    : value;
  imports[name] = module;
  imports[name.replace(/@[0-9.]+$/u, '')] = module;
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
// Read at once: what the sink holds when the run completes, before the host runs again.
const lines = output.split('\n');
const expected = Array.from({ length: 2000 }, (_, line) => `line ${line}`);
if (outcome.completionKind !== 'normal' || lines.length !== 2001 || lines.at(-1) !== '' ||
    lines.slice(0, -1).some((line, index) => line !== expected[index])) {
  console.error(`run completed ${outcome.completionKind} with ${lines.length - 1} of 2000 lines; last ${JSON.stringify(lines.slice(-3))}`);
  process.exit(1);
}
process.stdout.write('2000 lines\n');
