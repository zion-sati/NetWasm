import { sha256 } from "./sha256.mjs";

// The web actions every browser host takes. Each looks its browser API up when
// it runs, so a page whose browser lacks one still imports, and the failure
// surfaces from open() or run(), where the application can report it.
export const webPlatform = Object.freeze({
  compileCoreModule,
  createModuleUrl: bytes => URL.createObjectURL(new Blob([bytes], { type: "text/javascript" })),
  digest,
  fetch: (input, init) => globalThis.fetch(input, init),
  importModule: url => import(url),
  revokeModuleUrl: url => URL.revokeObjectURL(url),
});

export class NetWasmUnsupportedBrowserError extends Error {
  constructor(message, options) {
    super(message, options);
    this.name = "NetWasmUnsupportedBrowserError";
  }
}

const requirement = "NetWasm output needs Chrome or Edge 137, Firefox 131 or Safari 18.4, or later.";

// A function that uses the exception-handling (try_table) and SIMD
// instructions NetWasm output uses and the oldest supported browsers added.
const featureProbe = Uint8Array.of(
  0x00, 0x61, 0x73, 0x6d, 0x01, 0x00, 0x00, 0x00,
  0x01, 0x04, 0x01, 0x60, 0x00, 0x00,
  0x03, 0x02, 0x01, 0x00,
  0x0a, 0x1b, 0x01, 0x19, 0x00,
  0x1f, 0x40, 0x00, 0x0b,
  0xfd, 0x0c, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
  0x1a, 0x0b);

// A browser without these features rejects the module with a compile error
// that names an opcode; this names what the browser lacks instead.
async function compileCoreModule(bytes) {
  if (typeof WebAssembly !== "object" || WebAssembly === null) {
    throw new NetWasmUnsupportedBrowserError(`WebAssembly is unavailable in this browser. ${requirement}`);
  }
  try {
    return await WebAssembly.compile(bytes);
  } catch (error) {
    if (error?.name === "CompileError" && !WebAssembly.validate(featureProbe)) {
      throw new NetWasmUnsupportedBrowserError(
        `This browser does not support WebAssembly exception handling and SIMD. ${requirement}`,
        { cause: error });
    }
    throw error;
  }
}

// Browsers provide crypto.subtle only in a secure context, a page served over
// HTTPS or from localhost, so a page served over plain HTTP, such as a
// development build opened from a phone on the local network, hashes in
// JavaScript.
async function digest(algorithm, data) {
  if (algorithm !== "SHA-256") throw new TypeError(`unsupported digest algorithm '${algorithm}'`);
  const subtle = globalThis.crypto?.subtle;
  return subtle === undefined ? sha256(data) : subtle.digest(algorithm, data);
}
