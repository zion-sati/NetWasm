// Runs a command component under NetWasm's JavaScript host in a worker. The command writes a
// line, then never waits again; the parent stops the worker once the line arrives, as a page
// stops a run that takes too long, and fails if it never does (issue 224).
import { readFile } from 'node:fs/promises';
import { pathToFileURL } from 'node:url';
import { Worker, isMainThread, parentPort, workerData } from 'node:worker_threads';

if (isMainThread) {
  const [generatedRoot] = process.argv.slice(2);
  if (!generatedRoot) throw new Error('generated component directory is required');
  const worker = new Worker(new URL(import.meta.url), { workerData: { generatedRoot } });
  let output = '';
  const outcome = await new Promise(resolve => {
    const timer = setTimeout(() => resolve('no line within 30 s'), 30_000);
    worker.on('message', text => {
      output += text;
      if (output.includes('stalled\n')) {
        clearTimeout(timer);
        resolve('line');
      }
    });
    worker.on('error', error => {
      clearTimeout(timer);
      resolve(`worker failed: ${error.message}`);
    });
    worker.on('exit', code => {
      clearTimeout(timer);
      resolve(`worker exited with ${code}`);
    });
  });
  await worker.terminate();
  if (outcome !== 'line') {
    console.error(`${outcome}; output ${JSON.stringify(output)}`);
    process.exit(1);
  }
  process.stdout.write(output);
} else {
  const { commandComponentContract } = await import('../../../src/NetWasm.Hosting/JavaScript/canonical-component-binder.mjs');
  const { executeComponent } = await import('../../../src/NetWasm.Hosting/JavaScript/component-executor.mjs');
  const { createWasiPlatform } = await import('../../../src/NetWasm.Hosting/JavaScript/wasi-platform.mjs');
  const base = pathToFileURL(`${workerData.generatedRoot.replace(/\/$/u, '')}/`);
  const generated = await import(new URL('component.js', base));
  const loadCoreModule = file => readFile(new URL(file, base)).then(WebAssembly.compile);
  const decoder = new TextDecoder();
  const sink = Object.freeze({ write: bytes => parentPort.postMessage(decoder.decode(bytes, { stream: true })) });
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
  await executeComponent({ contractKey: commandComponentContract, adapter, imports, loadCoreModule });
}
