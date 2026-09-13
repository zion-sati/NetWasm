import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import test from "node:test";

import * as hosting from "./index.mjs";

test("exports only the selected bootstrap and host-local composition roots", async () => {
  assert.deepEqual(Object.keys(hosting), [
    "createBrowserNetWasmBootstrap",
    "createBrowserNetWasmExecution",
    "createLocalNetWasmExecution",
  ]);
  assert.equal(typeof hosting.createBrowserNetWasmBootstrap, "function");
  assert.equal(typeof hosting.createBrowserNetWasmExecution, "function");
  assert.equal(typeof hosting.createLocalNetWasmExecution, "function");
  const source = await readFile(new URL("./index.mjs", import.meta.url), "utf8");
  assert.doesNotMatch(source, /artifact-strategy|raw-execution|component-execution/u);
});
