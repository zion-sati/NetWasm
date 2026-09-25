import { createServer } from "node:http";
import { readFile } from "node:fs/promises";
import { chromium } from "playwright";

import { addRuntimeCompatibilityModules } from "../runtime-compatibility-module-map.mjs";

var [modulePath, manifestPath, adapterPath, target] = process.argv.slice(2);
var files = {
  "/": ["text/html", "<!doctype html><meta charset=\"utf-8\"><title>NetWasm mixed boundary</title>"],
  "/application.wasm": ["application/wasm", await readFile(modulePath)],
  "/manifest.json": ["application/json", await readFile(manifestPath)],
  "/mixed-boundary-contract.mjs": ["text/javascript", await readFile(
    new URL("./mixed-boundary-contract.mjs", import.meta.url))],
  "/NetWasm.Runtime.Tests/runtime-contract-imports.mjs": ["text/javascript", await readFile(
    new URL("../../NetWasm.Runtime.Tests/runtime-contract-imports.mjs", import.meta.url))],
  "/NetWasm.Runtime.Tests/yield-reactor-imports.mjs": ["text/javascript", await readFile(
    new URL("../../NetWasm.Runtime.Tests/yield-reactor-imports.mjs", import.meta.url))],
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
  await page.evaluate(async ({ compatibilityHostUrl, target }) => {
    var [{ instantiateNetWasm }, { runMixedBoundary }] = await Promise.all([
      import(compatibilityHostUrl),
      import("/mixed-boundary-contract.mjs"),
    ]);
    await runMixedBoundary(
      instantiateNetWasm,
      new Uint8Array(await (await fetch("/application.wasm")).arrayBuffer()),
      await (await fetch("/manifest.json")).json(),
      target);
  }, { compatibilityHostUrl, target });
  console.log(`Component Model mixed boundary ${target} Chromium PASS`);
} finally {
  await browser.close();
  await new Promise(resolve => server.close(resolve));
}
