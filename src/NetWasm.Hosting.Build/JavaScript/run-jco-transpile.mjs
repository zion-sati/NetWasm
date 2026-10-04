import { mkdir, readFile, writeFile } from "node:fs/promises";
import { dirname, isAbsolute, relative, resolve } from "node:path";
import { pathToFileURL } from "node:url";

const [apiPath, componentPath, outputDirectory, baseName, metadataPath] =
  process.argv.slice(2);
for (const value of [apiPath, componentPath, outputDirectory, metadataPath]) {
  if (typeof value !== "string" || !isAbsolute(value)) {
    throw new TypeError("jco transpile runner paths must be absolute");
  }
}
if (typeof baseName !== "string" || baseName.length === 0
    || baseName.includes("/") || baseName.includes("\\")) {
  throw new TypeError("jco transpile runner base name is invalid");
}

const namespace = await import(pathToFileURL(apiPath).href);
if (typeof namespace.transpile !== "function") {
  throw new TypeError("pinned jco transpile API is unavailable");
}
const result = await namespace.transpile(
  new Uint8Array(await readFile(componentPath)),
  {
    bindgenEnableWasmExnref: true,
    instantiation: "async",
    name: baseName,
    emitTypescriptDeclarations: false,
    quiet: true,
    strict: true,
    wasiShim: false,
  });
if (result === null || typeof result !== "object"
    || result.files === null || typeof result.files !== "object"
    || !Array.isArray(result.exports)) {
  throw new TypeError("pinned jco transpile result is invalid");
}

await mkdir(outputDirectory, { recursive: true });
const root = resolve(outputDirectory);
for (const [name, bytes] of Object.entries(result.files)) {
  const path = resolve(root, name);
  const child = relative(root, path);
  if (child.length === 0 || child.startsWith("..") || isAbsolute(child)
      || !(bytes instanceof Uint8Array)) {
    throw new TypeError("pinned jco transpile file is invalid");
  }
  await mkdir(dirname(path), { recursive: true });
  await writeFile(path, bytes);
}

const exports = result.exports.map(value => {
  if (!Array.isArray(value) || value.length !== 2
      || typeof value[0] !== "string" || value[0].length === 0
      || value[1] !== "function" && value[1] !== "instance") {
    throw new TypeError("pinned jco root export metadata is invalid");
  }
  return { name: value[0], kind: value[1] };
});
await writeFile(metadataPath, JSON.stringify({
  schemaVersion: 1,
  exports,
}));
