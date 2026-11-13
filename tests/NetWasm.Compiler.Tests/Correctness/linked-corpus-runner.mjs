import { createHash } from "node:crypto";
import { readFile, writeFile } from "node:fs/promises";
import { pathToFileURL } from "node:url";
import { instantiateNetWasm } from "../../../src/NetWasm.Runtime/browser-host.mjs";
import { createRuntimeContractImports } from "../../NetWasm.Runtime.Tests/runtime-contract-imports.mjs";
import { createMonotonicClockImports } from "../../NetWasm.Runtime.Tests/monotonic-clock-imports.mjs";
import { createReadOnlyAssetImports } from "../../NetWasm.Runtime.Tests/read-only-asset-imports.mjs";
import { createLinkedCorpusProgram } from "./linked-corpus-program.mjs";

export const linkedCorpusProgramDependencies = Object.freeze({
  readBinary: path => readFile(path),
  readText: path => readFile(path, "utf8"),
  compileModule: bytes => WebAssembly.compile(bytes),
  listImports: module => WebAssembly.Module.imports(module),
  hash: bytes => createHash("sha256").update(bytes).digest("hex"),
  instantiate: instantiateNetWasm,
  createRuntimeImports: (target, getMemory, getExports, sidecar) => ({
    ...createRuntimeContractImports(target, getMemory, getExports),
    ...createMonotonicClockImports(target, () => process.hrtime.bigint()),
    // The program's sidecar under /netwasm-sidecars, read through WASI 0.3's filesystem.
    ...(sidecar == null ? {} : createReadOnlyAssetImports(target, getMemory, (size, alignment) => {
      const address = target === "wasm64" ? BigInt : Number;
      return getExports().component_realloc(address(0), address(0), address(alignment), address(size));
    }, { mountPath: "/netwasm-sidecars", fileName: sidecar.fileName, bytes: sidecar.bytes })),
  }),
});
const program = createLinkedCorpusProgram(linkedCorpusProgramDependencies);

export const linkedCorpusCliDependencies = Object.freeze({
  readRequest: async path => JSON.parse(await readFile(path, "utf8")),
  run: program.run,
  writeResponse: (path, response) => writeFile(path, JSON.stringify(response)),
});

export async function runLinkedCorpusCli(args, {
  readRequest = linkedCorpusCliDependencies.readRequest,
  run = linkedCorpusCliDependencies.run,
  writeResponse = linkedCorpusCliDependencies.writeResponse,
} = linkedCorpusCliDependencies) {
  if (!Array.isArray(args) || args.length !== 2
      || args.some(value => typeof value !== "string" || value.length === 0))
    throw new TypeError("usage: linked-corpus-runner.mjs <request.json> <response.json>");
  const request = await readRequest(args[0]);
  const response = await run(request);
  await writeResponse(args[1], response);
}

export async function runLinkedCorpusMain({ moduleUrl, executablePath, args, run }) {
  if (executablePath && moduleUrl === pathToFileURL(executablePath).href)
    await run(args);
}

await runLinkedCorpusMain({
  moduleUrl: import.meta.url,
  executablePath: process.argv[1],
  args: process.argv.slice(2),
  run: runLinkedCorpusCli,
});
