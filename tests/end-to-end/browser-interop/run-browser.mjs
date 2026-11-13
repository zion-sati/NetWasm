import assert from "node:assert/strict";
import { createServer } from "node:http";
import { readFile, writeFile } from "node:fs/promises";
import { extname, relative, resolve } from "node:path";
import { chromium, firefox, webkit } from "playwright";

const [directory, kind, output, profile] = process.argv.slice(2);
const kinds = ["command", "worker", "wit", "page", "hosts", "timezone",
  "issue89-method-group", "issue89-finalization", "issue89-static-roots"];
assert.ok(directory && kinds.includes(kind) && output,
  `usage: run-browser.mjs <publish-directory> <${kinds.join("|")}> <result.json>`);
// The JsModule and run() fixtures are served under the strict policy the docs
// recommend, so a host change that needs more fails here.
const contentSecurityPolicy = ["page", "hosts", "timezone"].includes(kind)
  ? "default-src 'self'; script-src 'self' blob: 'wasm-unsafe-eval'"
  : null;
// Issue #139: the generated hosts work outside a secure context, where
// browsers have no crypto.subtle. These kinds load as http://netwasm.test/
// through a proxy to the server, which is not a secure context; the others'
// pages hash the deployment manifest with crypto.subtle themselves.
const insecure = ["page", "worker", "wit", "hosts", "timezone"].includes(kind);
const root = resolve(directory);
const types = { ".html": "text/html", ".mjs": "text/javascript", ".js": "text/javascript",
  ".wasm": "application/wasm", ".json": "application/json" };
const server = createServer(async (request, response) => {
  try {
    let pathname = decodeURIComponent(new URL(request.url, "http://localhost").pathname);
    // /tampered/ serves a copy of the deployment whose time zone sidecar is altered.
    const tampered = pathname.startsWith("/tampered/");
    if (tampered) pathname = pathname.slice("/tampered".length);
    const file = resolve(root, "." + (pathname === "/" ? "/index.html" : pathname));
    assert.ok(!relative(root, file).startsWith(".."));
    const bytes = await readFile(file);
    if (tampered && file.endsWith(".tz-info")) bytes[bytes.length - 1] ^= 0xff;
    const headers = { "content-type": types[extname(file)] ?? "application/octet-stream",
      "cache-control": "no-store" };
    if (contentSecurityPolicy !== null && extname(file) === ".html") {
      headers["content-security-policy"] = contentSecurityPolicy;
    }
    response.writeHead(200, headers);
    response.end(bytes);
  } catch {
    response.writeHead(404).end();
  }
});
// A slow runner (Debug on Windows Arm64) can wait more than Node's 5 s keep-alive between a page's
// requests; a browser reusing the connection the server just closed reported
// ERR_PROXY_CONNECTION_FAILED through the proxy. Idle connections stay open for the whole run.
server.keepAliveTimeout = 600_000;
server.headersTimeout = 601_000;
await new Promise(resolve => server.listen(0, "127.0.0.1", resolve));
const results = [];
try {
  const address = `http://127.0.0.1:${server.address().port}/`;
  for (const [name, engine] of [["chromium", chromium], ["firefox", firefox], ["webkit", webkit]]) {
    const entry = { browser: name, passed: false, pageErrors: [], consoleErrors: [] };
    results.push(entry);
    const browser = await engine.launch({ headless: true, ...(insecure ? { proxy: { server: address } } : {}) });
    let page;
    try {
      page = await browser.newPage();
      // A hang guard, not a deadline: WebKit runs emulated on Windows Arm64
      // runners, where a Debug cell can take longer than Playwright's 30 s.
      page.setDefaultTimeout(180_000);
      page.on("pageerror", error => entry.pageErrors.push(String(error)));
      page.on("console", message => {
        if (message.type() === "error") entry.consoleErrors.push(message.text());
      });
      entry.stage = "navigation";
      await page.goto(`${insecure ? "http://netwasm.test/" : address}?profile=${encodeURIComponent(profile ?? "")}`,
        { waitUntil: "domcontentloaded" });
      entry.secureContext = await page.evaluate(() => globalThis.isSecureContext);
      assert.equal(entry.secureContext, !insecure, `${kind}/${name} loaded in the wrong kind of context`);
      if (kind === "command") {
        entry.stage = "command-readiness";
        await page.waitForFunction(() => document.querySelector("#managed").textContent === "ready"
          || document.body.dataset.state === "failed");
        entry.stage = "callback";
        if (await page.locator("body").getAttribute("data-state") !== "failed")
          await page.locator("#deliver").click();
      }
      entry.stage = "completion";
      await page.waitForFunction(() => ["passed", "failed"].includes(document.body.dataset.state));
      entry.state = await page.locator("body").getAttribute("data-state");
      entry.testCase = await page.locator("body").getAttribute("data-test-case");
      entry.result = await page.locator("#result").textContent();
      const metrics = await page.locator("body").getAttribute("data-metrics");
      if (metrics !== null) entry.metrics = JSON.parse(metrics);
      entry.passed = entry.state === "passed" && entry.pageErrors.length === 0
        && entry.consoleErrors.length === 0;
      assert.equal(entry.passed, true, `${kind}/${name} failed; inspect the local result`);
    } catch (error) {
      entry.runnerError = String(error);
      if (page && !page.isClosed()) {
        entry.snapshot = await page.evaluate(() => ({
          state: document.body.dataset.state ?? null,
          managed: document.querySelector("#managed")?.textContent ?? null,
          released: document.querySelector("#released")?.textContent ?? null,
          result: document.querySelector("#result")?.textContent ?? null,
        })).catch(() => null);
      }
      throw error;
    } finally {
      await browser.close();
      await writeFile(output, JSON.stringify({ kind, results, passed: results.every(x => x.passed) }, null, 2));
    }
  }
} finally {
  await new Promise(resolve => server.close(resolve));
}
