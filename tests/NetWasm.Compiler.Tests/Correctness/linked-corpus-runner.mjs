import { createHash } from "node:crypto";
import { readFile, writeFile } from "node:fs/promises";
import { pathToFileURL } from "node:url";
import { instantiateNetWasm } from "../../../src/NetWasm.Runtime/browser-host.mjs";
import { createRuntimeContractImports } from "../../NetWasm.Runtime.Tests/runtime-contract-imports.mjs";
import { createTimerReactorImports } from "../../NetWasm.Runtime.Tests/timer-reactor-imports.mjs";
import { createLinkedCorpusProgram } from "./linked-corpus-program.mjs";

export const linkedCorpusProgramDependencies = Object.freeze({
  readBinary: path => readFile(path),
  readText: path => readFile(path, "utf8"),
  compileModule: bytes => WebAssembly.compile(bytes),
  listImports: module => WebAssembly.Module.imports(module),
  hash: bytes => createHash("sha256").update(bytes).digest("hex"),
  instantiate: instantiateNetWasm,
  createRuntimeImports: (target, getMemory, wake) => ({
    ...createRuntimeContractImports(target, getMemory),
    ...createTimerReactorImports(target, () => process.hrtime.bigint(),
      (milliseconds, callback) => setTimeout(callback, milliseconds), clearTimeout, wake),
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
