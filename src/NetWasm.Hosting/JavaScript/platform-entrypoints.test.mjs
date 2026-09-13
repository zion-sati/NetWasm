import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import test from "node:test";

import * as browser from "./browser.mjs";
import * as local from "./local.mjs";

test("browser and local entrypoints expose only their selected host roots", async () => {
  assert.deepEqual(Object.keys(browser).sort(), [
    "createBrowserNetWasmBootstrap",
    "createBrowserNetWasmExecution",
  ]);
  assert.deepEqual(Object.keys(local), ["createLocalNetWasmExecution"]);

  const browserSource = await readFile(new URL("./browser.mjs", import.meta.url), "utf8");
  assert.doesNotMatch(browserSource, /local/u);
  assert.doesNotMatch(browserSource, /node:/u);
  const localSource = await readFile(new URL("./local.mjs", import.meta.url), "utf8");
  assert.doesNotMatch(localSource, /browser/u);
});

test("deployment-selected roots do not import the unselected strategy", async () => {
  const modules = [
    ["browser-component-bootstrap.mjs", "createBrowserNetWasmBootstrap", /raw-/u],
    ["browser-component-execution-root.mjs", "createBrowserComponentNetWasmExecution", /raw-/u],
    ["browser-raw-bootstrap.mjs", "createBrowserNetWasmBootstrap", /component-/u],
    ["browser-raw-execution-root.mjs", "createBrowserRawNetWasmExecution", /component-/u],
    ["launcher-component.mjs", null, /raw-/u],
    ["launcher-raw.mjs", null, /component-/u],
    ["local-component-execution-root.mjs", "createLocalComponentNetWasmExecution", /raw-/u],
    ["local-raw-execution-root.mjs", "createLocalRawNetWasmExecution", /component-/u],
  ];
  for (const [name, exportName, forbidden] of modules) {
    const url = new URL(`./${name}`, import.meta.url);
    const source = await readFile(url, "utf8");
    assert.doesNotMatch(source, forbidden, `${name} imports an unselected strategy`);
    if (exportName !== null) {
      assert.deepEqual(Object.keys(await import(url)), [exportName]);
    }
  }
});
