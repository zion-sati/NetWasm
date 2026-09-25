import fs from "node:fs";
import { pathToFileURL } from "node:url";
import { runMixedBoundary } from "./mixed-boundary-contract.mjs";

var [modulePath, manifestPath, adapterPath, target] = process.argv.slice(2);
var { instantiateNetWasm } = await import(pathToFileURL(adapterPath));
await runMixedBoundary(
  instantiateNetWasm,
  fs.readFileSync(modulePath),
  JSON.parse(fs.readFileSync(manifestPath, "utf8")),
  target);
console.log(`Component Model mixed boundary ${target} Node PASS`);
