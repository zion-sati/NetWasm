// Trusted tool host only. Its MEMFS and loader are not guest capabilities.
const encoder = new TextEncoder();
const root = '/netwasm-link';
function virtualPath(path) {
  if (typeof path !== 'string' || !path.startsWith(`${root}/`) ||
      path.includes('\0') || path.split('/').some(p => p === '..' || p === '.'))
    throw new Error(`Tool path must be inside ${root}`);
  return path;
}

export function createBrowserLld(moduleFactory, {
  maximumInputBytes = 268435456, maximumOutputBytes = 268435456,
  maximumDiagnosticBytes = 65536, maximumArgumentBytes = 1048576,
} = {}) {
  let module, busy = false;
  let stdout = '', stderr = '', diagnosticBytes = 0;
  function append(which, line) {
    const text = `${line}\n`;
    const remaining = Math.max(0, maximumDiagnosticBytes - diagnosticBytes);
    const bytes = encoder.encode(text).subarray(0, remaining);
    diagnosticBytes += bytes.byteLength;
    if (which === 'stdout') stdout += new TextDecoder().decode(bytes, { stream: true });
    else stderr += new TextDecoder().decode(bytes, { stream: true });
  }
  return {
    async link({ arguments: args, files, outputPath }) {
      if (busy) throw new Error('Only one LLD invocation may be active');
      busy = true;
      stdout = ''; stderr = ''; diagnosticBytes = 0;
      let pointers = [], argvPointer, createdFiles = [], createdDirectories = [];
      let active;
      try {
        virtualPath(outputPath);
        if (!Array.isArray(args) || args.some(a => typeof a !== 'string' || a.includes('\0')))
          throw new Error('LLD arguments must be strings without NUL');
        if (args.reduce((sum, a) => sum + encoder.encode(a).byteLength + 1, 0) > maximumArgumentBytes)
          throw new Error('LLD argument byte limit exceeded');
        if (Object.hasOwn(files, outputPath)) throw new Error('LLD output path cannot be an input');
        let total = 0;
        for (const [path, bytes] of Object.entries(files)) {
          virtualPath(path);
          if (!(bytes instanceof Uint8Array)) throw new Error('LLD files must be Uint8Array');
          total += bytes.byteLength;
        }
        if (total > maximumInputBytes) throw new Error('LLD input byte limit exceeded');
        args = args.slice();
        files = Object.fromEntries(Object.entries(files).map(([path, bytes]) => [path, bytes.slice()]));
        if (!module) module = await moduleFactory({
          print: line => append('stdout', line), printErr: line => append('stderr', line),
        });
        active = module;
        function mkdir(path) {
          const segments = path.split('/').filter(Boolean); let current = '';
          for (const segment of segments) {
            current += `/${segment}`;
            if (!active.FS.analyzePath(current).exists) {
              active.FS.mkdir(current); createdDirectories.push(current);
            }
          }
        }
        mkdir(outputPath.slice(0, outputPath.lastIndexOf('/')));
        for (const [path, bytes] of Object.entries(files)) {
          mkdir(path.slice(0, path.lastIndexOf('/')));
          active.FS.writeFile(path, bytes); createdFiles.push(path);
        }
        createdFiles.push(outputPath);
        const argv = ['wasm-ld', ...args];
        for (const arg of argv) {
          const length = active.lengthBytesUTF8(arg) + 1;
          const pointer = active._malloc(length);
          if (!pointer) throw new Error('LLD argument allocation failed');
          pointers.push(pointer); active.stringToUTF8(arg, pointer, length);
        }
        argvPointer = active._malloc((pointers.length + 1) * 4);
        if (!argvPointer) throw new Error('LLD argv allocation failed');
        const heap = new Uint32Array(active.HEAPU8.buffer);
        pointers.forEach((pointer, index) => { heap[(argvPointer >>> 2) + index] = pointer; });
        heap[(argvPointer >>> 2) + pointers.length] = 0;
        const started = performance.now();
        const status = active._netwasm_lld_link(pointers.length, argvPointer);
        const milliseconds = performance.now() - started;
        const canRunAgain = !(status & 2);
        // LLVM's Wasm LTO state currently survives a successful lldMain call.
        // Fresh instances also honor canRunAgain:false and failure recovery.
        module = undefined;
        if (status & 1) return { success: false, canRunAgain, stdout, stderr, milliseconds };
        if (active.FS.stat(outputPath).size > maximumOutputBytes)
          throw new Error('LLD output byte limit exceeded');
        const bytes = active.FS.readFile(outputPath);
        return { success: true, canRunAgain, bytes, stdout, stderr, milliseconds,
          linearMemoryBytes: active.HEAPU8.buffer.byteLength };
      } catch (error) {
        module = undefined;
        throw error;
      } finally {
        if (active) {
          try {
            for (const pointer of pointers) active._free(pointer);
            if (argvPointer) active._free(argvPointer);
            for (const path of [...new Set(createdFiles)].reverse())
              if (active.FS.analyzePath(path).exists) active.FS.unlink(path);
            for (const path of createdDirectories.reverse()) active.FS.rmdir(path);
          } catch { module = undefined; }
        }
        busy = false;
      }
    },
    dispose() { if (busy) throw new Error('Terminate the worker to cancel active LLD'); module = undefined; },
  };
}
