import { createServer } from "node:http";
import { readFile } from "node:fs/promises";
import { chromium } from "playwright";

import { addRuntimeCompatibilityModules } from "../runtime-compatibility-module-map.mjs";

var [modulePath, manifestPath, adapterPath, target] = process.argv.slice(2);
if (!modulePath || !manifestPath || !adapterPath || !target) {
  throw new Error(
    "usage: run-async-js-playwright.mjs <module> <manifest> <adapter> <wasm32|wasm64>");
}

var files = {
  "/": ["text/html", "<!doctype html><meta charset=\"utf-8\"><title>NetWasm async interop</title>"],
  "/application.wasm": ["application/wasm", await readFile(modulePath)],
  "/manifest.json": ["application/json", await readFile(manifestPath)],
  "/async-js-contract.mjs": ["text/javascript", await readFile(
    new URL("./async-js-contract.mjs", import.meta.url))],
  "/NetWasm.Runtime.Tests/runtime-contract-imports.mjs": ["text/javascript", await readFile(
    new URL("../../NetWasm.Runtime.Tests/runtime-contract-imports.mjs", import.meta.url))],
  "/NetWasm.Runtime.Tests/yield-reactor-imports.mjs": ["text/javascript", await readFile(
    new URL("../../NetWasm.Runtime.Tests/yield-reactor-imports.mjs", import.meta.url))],
  "/NetWasm.Runtime.Tests/output-stream-imports.mjs": ["text/javascript", await readFile(
    new URL("../../NetWasm.Runtime.Tests/output-stream-imports.mjs", import.meta.url))],
};
var compatibilityHostUrl = await addRuntimeCompatibilityModules(files, adapterPath);
var server = createServer((request, response) => {
  var entry = files[request.url];
  if (!entry) {
    response.writeHead(404).end();
    return;
  }
  response.writeHead(200, {
    "content-type": entry[0],
    "content-security-policy": "default-src 'none'; script-src 'self' 'wasm-unsafe-eval'; connect-src 'self'",
    "cache-control": "no-store",
  });
  response.end(entry[1]);
});

await new Promise((resolve, reject) => {
  server.once("error", reject);
  server.listen(0, "127.0.0.1", resolve);
});
var browser = await chromium.launch({ headless: true });
try {
  var page = await browser.newPage();
  await page.goto(`http://127.0.0.1:${server.address().port}/`, {
    waitUntil: "domcontentloaded",
  });
  await page.evaluate(async compatibilityHostUrl => {
    var [{ instantiateNetWasm, NetWasmManagedError }, { runAsyncJavaScriptInterop }] =
      await Promise.all([
        import(compatibilityHostUrl),
        import("/async-js-contract.mjs"),
      ]);
    var module = new Uint8Array(
      await (await fetch("/application.wasm")).arrayBuffer());
    var manifest = await (await fetch("/manifest.json")).json();
    await runAsyncJavaScriptInterop(
      instantiateNetWasm,
      NetWasmManagedError,
      module,
      manifest);
  }, compatibilityHostUrl);
  console.log(`Component Model async JavaScript interop ${target} Chromium PASS`);
} finally {
  await browser.close();
  await new Promise(resolve => server.close(resolve));
}
