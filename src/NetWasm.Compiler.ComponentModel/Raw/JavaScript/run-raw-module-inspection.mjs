import { readFile } from "node:fs/promises";
import { pathToFileURL } from "node:url";
import { readBinaryenFunctionImports } from "./binaryen-function-import-reader.mjs";
import { readCoreModuleImports } from "./core-module-import-reader.mjs";
import { runRawModuleInspectionCommand } from "./raw-module-inspection-command.mjs";
import { inspectRawModuleImports } from "./raw-module-import-inspector.mjs";

process.exitCode = await runRawModuleInspectionCommand(process.argv.slice(2), {
  readModule: path => readFile(path),
  async loadDecoder(path) {
    const module = await import(pathToFileURL(path).href);
    return module.default;
  },
  inspectModule: (bytes, decoder) => inspectRawModuleImports(bytes, {
    readCoreImports: input => readCoreModuleImports(input, WebAssembly),
    readFunctionImports: input => readBinaryenFunctionImports(input, decoder),
  }),
  writeOutput: output => process.stdout.write(output),
});
