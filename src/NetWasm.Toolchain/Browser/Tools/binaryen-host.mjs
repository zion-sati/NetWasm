// NetWasm Community License 1.0; pinned Binaryen code keeps its upstream license.
import { toolLimits, snapshotRequest, loadToolAsset, diagnostics, copyOutputs } from './tool-inputs.mjs';
const decoder = new TextDecoder();

/** Remove only the pinned CLI's NODERAWFS installation and path binding.
 * The embedded Wasm and command implementation remain byte-for-byte intact. */
async function bootstrap(source) {
  source = source.replace(/^#![^\n]*\n/, '');
  const pathNeedle = 'var nodePath=require("node:path");';
  const startNeedle = 'if(!ENVIRONMENT_IS_NODE){throw new Error("NODERAWFS is currently only supported on Node.js environment.")}var nodeTTY=require("node:tty");';
  const endNeedle = '{if(Module["noExitRuntime"])';
  const start = source.indexOf(startNeedle), end = source.indexOf(endNeedle, start);
  if (source.split(pathNeedle).length !== 2 || source.split(startNeedle).length !== 2
      || start < 0 || end <= start || end - start !== 698
      || !source.slice(start, end).endsWith('for(const[key,value]of Object.entries(NODERAWFS_stream_funcs)){FS[key]=_wrapNodeStreamFunc(value,FS[key])}'))
    throw Error('Pinned Binaryen bootstrap shape changed');
  source = source.slice(0, start) + source.slice(end);
  source = source.replace(pathNeedle, 'var nodePath=Module.browserPath;');
  return importFactory(`export default function(Module) {\n${source}\nreturn { FS, callMain, memory: () => wasmMemory.buffer.byteLength, exit: () => EXITSTATUS };\n}`);
}

/** Import only caller-verified trusted code. Revoke the source URL after module
 * evaluation; the cached factory keeps its compiled module alive. */
async function importFactory(source) {
  const url = URL.createObjectURL(new Blob([source], { type: 'text/javascript' }));
  try { return (await import(url)).default; }
  finally { URL.revokeObjectURL(url); }
}

/** @param {{loadAsset: import('./tool-inputs.mjs').VerifiedAssetLoader,
 * limits?: object, onEnter?: (tool: string) => void}} options */
export function createBinaryenHost({ loadAsset, limits: options, onEnter }) {
  const limits = toolLimits(options), factories = new Map();
  let pathFactory, busy = false;
  return {
    /** @param {'wasm-merge'|'wasm-opt'} tool
     * @param {import('./tool-inputs.mjs').ToolRequest} request
     * @returns {Promise<import('./tool-inputs.mjs').ToolResult>} */
    async run(tool, request) {
      if (tool !== 'wasm-merge' && tool !== 'wasm-opt') throw Error('Unknown Binaryen tool');
      if (busy) throw Error('Only one Binaryen invocation may be active');
      busy = true;
      let runtime, args, files, outputs;
      try {
        ({ args, files, outputs } = snapshotRequest(request, limits));
        const log = diagnostics(limits);
        // Cache only successful imports so a rejected loader or module can retry.
        if (!factories.has(tool)) factories.set(tool, await bootstrap(decoder.decode(await loadToolAsset(loadAsset, `${tool}.js`, limits))));
        if (!pathFactory) pathFactory = await importFactory(`export default function(module, exports, process) {\n${
          decoder.decode(await loadToolAsset(loadAsset, 'path-browserify.js', limits))}\n}`);
        const pathModule = { exports: {} };
        pathFactory(pathModule, pathModule.exports, { cwd: () => '/' });
        let resolveReady, rejectReady;
        const ready = new Promise((resolve, reject) => { resolveReady = resolve; rejectReady = reject; });
        runtime = factories.get(tool)({ browserPath: pathModule.exports, noInitialRun: true,
          print: text => log.line('stdout', text), printErr: text => log.line('stderr', text),
          onRuntimeInitialized: () => resolveReady(), onAbort: reason => rejectReady(Error(String(reason))) });
        await ready;
        for (const [name, bytes] of Object.entries(files)) runtime.FS.writeFile(name, bytes);
        let exitCode, failure;
        onEnter?.(tool);
        try { exitCode = runtime.callMain(args); }
        catch (error) {
          const status = runtime.exit();
          exitCode = Number.isInteger(status) && status !== 0 ? status : 1;
          failure = String(error);
        }
        log.check();
        const result = exitCode === 0 ? copyOutputs(outputs, name => {
          if (runtime.FS.stat(name).size > limits.maximumOutputBytes) throw Error('Tool output limit exceeded');
          return runtime.FS.readFile(name);
        }, limits) : {};
        return { exitCode, failure, files: result, memoryBytes: runtime.memory(), ...log.result() };
      } finally {
        if (runtime) for (const name of [...Object.keys(files || {}), ...(outputs || [])]) {
          try { runtime.FS.unlink(name); } catch {}
        }
        busy = false;
      }
    },
  };
}
