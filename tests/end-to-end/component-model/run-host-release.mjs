// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

// Runs HostReleaseProgram under NetWasm's JavaScript host in a worker whose JavaScript heap is capped at
// 128 MB. The program reads 400 MB, writes 400 MB in 100,000 writes and cancels 50,000 timers: a host that
// keeps what each finished call held runs out of memory long before the program ends. It then opens a file
// 1,000 times; once the run is over, the host holds no file descriptor beyond the directory it mounted. Last it
// sleeps 1 ms 200 times: the generated runtime polls an idle scheduler every 10 ms, and a host wait that ends must
// wake it, so at most a few of those polls may fire (each sleep waited for one before the scheduler was woken).
import { mkdtemp, readFile, rm, writeFile } from 'node:fs/promises';
import * as fs from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { pathToFileURL } from 'node:url';
import { getHeapSnapshot } from 'node:v8';
import { Worker, isMainThread, parentPort, workerData } from 'node:worker_threads';

const fileBytes = 4 * 1024 * 1024;
const expected = [
  `read ${fileBytes * 100}`,
  `written ${4096 * 100_000}`,
  'cancelled 50000',
  'opened 1000',
  'slept 200',
  // Read after the run: the descriptor the mounted directory holds, and no other.
  'live descriptors 1',
  'idle polls few',
].join('\n') + '\n';
// The generated runtime's idle scheduler polls with a 10 ms timer.
const idlePollDelay = 10;

if (isMainThread) {
  const [generatedRoot] = process.argv.slice(2);
  if (!generatedRoot) throw new Error('generated component directory is required');
  const dataDirectory = await mkdtemp(join(tmpdir(), 'netwasm-host-release-'));
  try {
    await writeFile(join(dataDirectory, 'data.bin'), Buffer.alloc(fileBytes, 7));
    const worker = new Worker(new URL(import.meta.url), {
      workerData: { generatedRoot, dataDirectory },
      resourceLimits: { maxOldGenerationSizeMb: 128 },
    });
    let output = '';
    const outcome = await new Promise(resolve => {
      worker.on('message', text => { output += text; });
      worker.on('error', error => resolve(`worker failed: ${error.code ?? ''} ${error.message}`));
      worker.on('exit', code => resolve(code === 0 ? 'completed' : `worker exited with ${code}`));
    });
    if (outcome !== 'completed' || output !== expected) {
      console.error(`${outcome}; output ${JSON.stringify(output)}, expected ${JSON.stringify(expected)}`);
      process.exit(1);
    }
    process.stdout.write(output);
  } finally {
    await rm(dataDirectory, { recursive: true, force: true });
  }
} else {
  const { generatedRoot, dataDirectory } = workerData;
  // Counts the idle polls that fire, before the generated runtime loads.
  let idlePolls = 0;
  const setTimer = globalThis.setTimeout;
  globalThis.setTimeout = (callback, delay, ...rest) => setTimer(delay === idlePollDelay
    ? (...values) => { idlePolls++; return callback(...values); }
    : callback, delay, ...rest);
  const { commandComponentContract } = await import('../../../src/NetWasm.Hosting/JavaScript/canonical-component-binder.mjs');
  const { executeComponent } = await import('../../../src/NetWasm.Hosting/JavaScript/component-executor.mjs');
  const { createWasiPlatform } = await import('../../../src/NetWasm.Hosting/JavaScript/wasi-platform.mjs');
  const { createWasiFilesystem } = await import('../../../src/NetWasm.Hosting/JavaScript/wasi-filesystem.mjs');
  const base = pathToFileURL(`${generatedRoot.replace(/\/$/u, '')}/`);
  const generated = await import(new URL('component.js', base));
  const loadCoreModule = file => readFile(new URL(file, base)).then(WebAssembly.compile);
  const decoder = new TextDecoder();
  const sink = Object.freeze({ write: bytes => parentPort.postMessage(decoder.decode(bytes, { stream: true })) });
  const hostFiles = Object.freeze({
    closeSync: fs.closeSync, ftruncateSync: fs.ftruncateSync, lstatSync: fs.lstatSync, mkdirSync: fs.mkdirSync,
    openSync: fs.openSync, readdirSync: fs.readdirSync, readlinkSync: fs.readlinkSync, readSync: fs.readSync,
    realpathSync: fs.realpathSync, renameSync: fs.renameSync, rmdirSync: fs.rmdirSync, statSync: fs.statSync,
    symlinkSync: fs.symlinkSync, unlinkSync: fs.unlinkSync, utimesSync: fs.utimesSync, writeSync: fs.writeSync,
  });
  const filesystem = createWasiFilesystem(Object.freeze({
    hostFiles,
    preopens: Object.freeze([Object.freeze({ hostPath: dataDirectory, guestPath: '/data', access: 'readWrite' })]),
  }));
  const platform = createWasiPlatform(Object.freeze({ initialDirectory: null, network: null }))(Object.freeze({
    arguments: Object.freeze([]),
    environment: Object.freeze([]),
    filesystem,
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
  if (outcome.completionKind !== 'normal') throw new Error(`the run completed ${outcome.completionKind}`);
  // The generated bindings stay loaded, and with them every table they keep resources in.
  parentPort.postMessage(`live descriptors ${await countLive('Descriptor')}\n`);
  parentPort.postMessage(`idle polls ${idlePolls <= 20 ? 'few' : idlePolls}\n`);
  if (typeof generated.instantiate !== 'function') throw new Error('the generated bindings were unloaded');
}

// Counts the live objects of a class in a heap snapshot, which collects garbage first.
async function countLive(className) {
  let text = '';
  for await (const chunk of getHeapSnapshot()) text += chunk;
  const { snapshot, nodes, strings } = JSON.parse(text);
  const fields = snapshot.meta.node_fields;
  const nameAt = fields.indexOf('name');
  const typeAt = fields.indexOf('type');
  const objectType = snapshot.meta.node_types[0].indexOf('object');
  let live = 0;
  for (let node = 0; node < nodes.length; node += fields.length) {
    if (nodes[node + typeAt] === objectType && strings[nodes[node + nameAt]] === className) live++;
  }
  return live;
}
