import { existsSync, statSync } from "node:fs";
import { readFile } from "node:fs/promises";
import path from "node:path";
import { WASI } from "node:wasi";

const [modulePath, ...requestedArguments] = process.argv.slice(2);
if (!modulePath) {
  throw new Error("The NetWasm wasm-tools runner requires a module path.");
}

const preopens = { ".": process.cwd() };
const mountedRoots = new Map();
const argumentsForWasi = requestedArguments.map(mapAbsolutePath);

function mapAbsolutePath(argument) {
  if (!path.isAbsolute(argument)) {
    return argument;
  }

  const hostRoot = findExistingRoot(argument);
  const key = process.platform === "win32" ? hostRoot.toLowerCase() : hostRoot;
  let guestRoot = mountedRoots.get(key);
  if (!guestRoot) {
    guestRoot = `/netwasm/paths/${mountedRoots.size}`;
    mountedRoots.set(key, guestRoot);
    preopens[guestRoot] = hostRoot;
  }

  const relativePath = path.relative(hostRoot, argument)
    .split(path.sep)
    .join("/");
  return relativePath.length === 0 ? guestRoot : `${guestRoot}/${relativePath}`;
}

function findExistingRoot(absolutePath) {
  let candidate = existsSync(absolutePath) && statSync(absolutePath).isDirectory()
    ? absolutePath
    : path.dirname(absolutePath);
  while (!existsSync(candidate)) {
    const parent = path.dirname(candidate);
    if (parent === candidate) {
      throw new Error(`No existing host directory contains '${absolutePath}'.`);
    }
    candidate = parent;
  }
  return candidate;
}

const wasi = new WASI({
  version: "preview1",
  returnOnExit: true,
  args: ["wasm-tools", ...argumentsForWasi],
  env: {},
  preopens,
});
const module = await WebAssembly.compile(await readFile(modulePath));
const instance = await WebAssembly.instantiate(module, wasi.getImportObject());
const exitCode = wasi.start(instance);
if (!Number.isSafeInteger(exitCode) || exitCode < 0 || exitCode > 255) {
  throw new Error("The NetWasm wasm-tools runner received an invalid exit code.");
}
process.exitCode = exitCode;
