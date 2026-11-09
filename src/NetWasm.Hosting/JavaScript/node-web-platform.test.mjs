import assert from "node:assert/strict";
import { createHash } from "node:crypto";
import { mkdtemp, rm, writeFile } from "node:fs/promises";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { pathToFileURL } from "node:url";
import test from "node:test";

import { nodePlatform } from "./node-web-platform.mjs";

const emptyModule = Uint8Array.of(0x00, 0x61, 0x73, 0x6d, 0x01, 0x00, 0x00, 0x00);

test("compiles, hashes and imports as a browser host does", async () => {
  assert.equal(Object.isFrozen(nodePlatform), true);
  assert.ok(await nodePlatform.compileCoreModule(emptyModule) instanceof WebAssembly.Module);
  const bytes = new TextEncoder().encode("export const answer = 42;");
  assert.deepEqual(
    Buffer.from(await nodePlatform.digest("SHA-256", bytes)).toString("hex"),
    createHash("sha256").update(bytes).digest("hex"));
  const url = nodePlatform.createModuleUrl(bytes, "text/javascript");
  assert.match(url, /^data:text\/javascript;base64,/u);
  assert.equal((await nodePlatform.importModule(url)).answer, 42);
  assert.equal(nodePlatform.revokeModuleUrl(url), undefined);
});

test("reads a published file and fetches anything else", async () => {
  const root = await mkdtemp(join(tmpdir(), "netwasm-node-platform-"));
  const original = globalThis.fetch;
  try {
    const path = join(root, "deployment.json");
    await writeFile(path, "{}");
    const response = await nodePlatform.fetch(pathToFileURL(path).href, {});
    assert.equal(response.ok, true);
    assert.equal(await response.text(), "{}");
    assert.equal(await (await nodePlatform.fetch(pathToFileURL(path))).text(), "{}");
    const controller = new AbortController();
    controller.abort();
    await assert.rejects(
      () => nodePlatform.fetch(pathToFileURL(path).href, { signal: controller.signal }),
      { name: "AbortError" });
    await assert.rejects(() => nodePlatform.fetch(pathToFileURL(join(root, "missing")).href), /ENOENT/);

    const calls = [];
    globalThis.fetch = async (input, init) => { calls.push([input, init]); return "response"; };
    const init = { method: "POST" };
    assert.equal(await nodePlatform.fetch("https://example.test/", init), "response");
    assert.deepEqual(calls, [["https://example.test/", init]]);
  } finally {
    globalThis.fetch = original;
    await rm(root, { recursive: true, force: true });
  }
});
