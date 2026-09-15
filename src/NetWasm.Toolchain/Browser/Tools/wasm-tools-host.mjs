// Trusted tool-private Preview 1 host, never a user-program WASI implementation.
import { toolLimits, snapshotRequest, loadToolAsset, diagnostics, copyOutputs } from './tool-inputs.mjs';

/**
 * @param {{loadAsset: import('./tool-inputs.mjs').VerifiedAssetLoader,
 * wasiShim: object, limits?: object, onEnter?: (tool: string) => void}} options
 */
export function createWasmToolsHost({ loadAsset, wasiShim: shim, limits: options, onEnter }) {
  const limits = toolLimits(options);
  for (const name of ['WASI', 'PreopenDirectory', 'File', 'OpenFile', 'ConsoleStdout'])
    if (typeof shim?.[name] !== 'function') throw Error(`Missing trusted WASI shim export: ${name}`);
  let compiled, busy = false;
  return {
    /** @param {import('./tool-inputs.mjs').ToolRequest} request
     * @returns {Promise<import('./tool-inputs.mjs').ToolResult>} */
    async run(request) {
      if (busy) throw Error('Only one wasm-tools invocation may be active');
      busy = true;
      let root, wasi;
      try {
        const { args, files, outputs } = snapshotRequest(request, limits);
        const log = diagnostics(limits);
        if (!compiled) compiled = WebAssembly.compile(await loadToolAsset(loadAsset, 'wasm-tools.wasm', limits));
        const module = await compiled;
        root = new shim.PreopenDirectory('.', Object.entries(files).map(([name, bytes]) => [name, new shim.File(bytes)]));
        const argv = ['wasm-tools', ...args];
        wasi = new shim.WASI(argv, [], [new shim.OpenFile(new shim.File([])),
          new shim.ConsoleStdout(bytes => log.append('stdout', bytes)),
          new shim.ConsoleStdout(bytes => log.append('stderr', bytes)), root]);
        const calls = {};
        const imports = Object.fromEntries(Object.entries(wasi.wasiImport).map(([name, fn]) => [name, (...argv) => {
          calls[name] = (calls[name] || 0) + 1;
          return fn(...argv);
        }]));
        // Shim 0.4.2 sizes argv by JS character count but writes UTF-8 bytes.
        // Correct the allocation size while preserving its args_get semantics.
        imports.args_sizes_get = (argc, argvBytes) => {
          calls.args_sizes_get = (calls.args_sizes_get || 0) + 1;
          const view = new DataView(wasi.inst.exports.memory.buffer);
          view.setUint32(argc, argv.length, true);
          view.setUint32(argvBytes, argv.reduce((sum, arg) => sum + new TextEncoder().encode(arg).byteLength + 1, 0), true);
          return 0;
        };
        const instance = await WebAssembly.instantiate(module, { wasi_snapshot_preview1: imports });
        onEnter?.('wasm-tools');
        const exitCode = wasi.start(instance);
        log.check();
        const result = exitCode === 0 ? copyOutputs(outputs, name => {
          const file = root.dir.contents.get(name);
          if (!(file instanceof shim.File)) throw Error(`Missing output ${name}`);
          return file.data;
        }, limits) : {};
        return { exitCode, files: result, memoryBytes: instance.exports.memory.buffer.byteLength,
          calls, ...log.result() };
      } catch (error) {
        // A failed compilation promise must not poison a later loader retry.
        if (compiled) compiled.catch(() => { compiled = undefined; });
        throw error;
      } finally {
        try {
          if (wasi) for (const fd of wasi.fds) fd?.fd_close();
        } finally {
          if (wasi) { wasi.fds.length = 0; wasi.inst = undefined; }
          root?.dir.contents.clear();
          busy = false;
        }
      }
    },
  };
}
