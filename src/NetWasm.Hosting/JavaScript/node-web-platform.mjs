import { readFile } from "node:fs/promises";
import { fileURLToPath } from "node:url";

// The browser host's web actions for a host that Node.js code imports: the published files
// are read from disk, a verified module imports from a data: URL (Node.js cannot import a
// blob: URL), and any other request is an ordinary fetch.
export const nodePlatform = Object.freeze({
  compileCoreModule: bytes => WebAssembly.compile(bytes),
  createModuleUrl: bytes => `data:text/javascript;base64,${Buffer.from(bytes).toString("base64")}`,
  digest: (algorithm, data) => globalThis.crypto.subtle.digest(algorithm, data),
  fetch: (input, init) => String(input).startsWith("file:")
    ? fetchFile(String(input), init)
    : globalThis.fetch(input, init),
  importModule: url => import(url),
  revokeModuleUrl() {},
});

async function fetchFile(url, init) {
  return new Response(await readFile(fileURLToPath(url), { signal: init?.signal ?? undefined }));
}
