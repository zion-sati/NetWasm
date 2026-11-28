// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: MIT

// Writes the runtime layout for a runtime built outside a compile (the runtime pack, the runtime's own tests): every
// runtime feature the compiler can record, so eng/build-netwasm-runtime.sh keeps the complete runtime, and no native
// imports or host modules.
// Usage: node write-complete-runtime-layout.mjs <output> <wasm32|wasm64> [applicationStaticDataEnd]
import fs from "node:fs";
import path from "node:path";
import { fileURLToPath, pathToFileURL } from "node:url";

const [output, target, staticDataEnd = "0"] = process.argv.slice(2);
const applicationStaticDataEnd = Number(staticDataEnd);
if (!output || (target !== "wasm32" && target !== "wasm64")
    || !Number.isSafeInteger(applicationStaticDataEnd) || applicationStaticDataEnd < 0) {
  process.stderr.write("usage: write-complete-runtime-layout.mjs <output> <wasm32|wasm64> [applicationStaticDataEnd]\n");
  process.exit(2);
}
const featureIds = path.join(path.dirname(fileURLToPath(import.meta.url)),
  "../../NetWasm.Hosting/JavaScript/runtime-feature-ids.mjs");
const { runtimeFeatureIds } = await import(pathToFileURL(featureIds).href);
fs.writeFileSync(output, `${JSON.stringify({
  schemaVersion: 3,
  target,
  applicationStaticDataEnd,
  managedExecutableEntryPoint: null,
  runtimeFeatures: [...runtimeFeatureIds],
  nativeImports: [],
  importModules: [],
}, null, 2)}\n`);
