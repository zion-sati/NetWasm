import fs from "node:fs";
import { pathToFileURL } from "node:url";
import { runAsyncJavaScriptInterop } from "./async-js-contract.mjs";

var [wasmPath, manifestPath, adapterPath, target] = process.argv.slice(2);
var { instantiateNetWasm, NetWasmManagedError } = await import(pathToFileURL(adapterPath));
await runAsyncJavaScriptInterop(
  instantiateNetWasm,
  NetWasmManagedError,
  fs.readFileSync(wasmPath),
  JSON.parse(fs.readFileSync(manifestPath, "utf8")));
console.log(`Component Model async JavaScript interop ${target ?? "unknown target"} Node PASS`);
