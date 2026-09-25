import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import { join } from "node:path";
import { pathToFileURL } from "node:url";
import { WASIShim } from "@bytecodealliance/preview2-shim/instantiation";

const [directory, statusArgument, exitCodeArgument] = process.argv.slice(2);
const expectedStatus = Number(statusArgument);
const expectedExitCode = Number(exitCodeArgument);
assert.ok([1, 2, 3].includes(expectedStatus));
const component = await import(pathToFileURL(join(directory, "managed-process.js")));
const shim = new WASIShim({ sandbox: { args: [], env: {}, preopens: {}, enableNetwork: false } });
const versioned = shim.getImportObject({ asVersion: "0.2.11" });
const imports = {};
for (const [name, service] of Object.entries(versioned)) {
  assert.match(name, /^wasi:.*@0\.2\.11$/);
  imports[name] = service;
  imports[name.replace(/@0\.2\.11$/, "")] = service;
}
let root;
let handle;
let watchCount = 0;
let wakeCount = 0;
const pending = new Map();
let resolveCompletion;
let rejectCompletion;
const completed = new Promise((resolve, reject) => {
  resolveCompletion = resolve;
  rejectCompletion = reject;
});
const reactor = {
  watch(pollable, token) {
    assert.equal(pending.has(token), false);
    pending.set(token, pollable);
    watchCount++;
    Promise.resolve().then(() => pollable.block()).then(() => {
      if (!pending.delete(token)) return;
      pollable[Symbol.dispose]();
      root.reactorGuest.wake(token);
      wakeCount++;
      const status = root.process.status(handle);
      if (status !== 0) {
        assert.equal(status, expectedStatus);
        resolveCompletion();
      }
    }).catch(rejectCompletion);
  },
  cancel(token) {
    const pollable = pending.get(token);
    if (pending.delete(token)) pollable[Symbol.dispose]();
  },
};
imports["netwasm:runtime/reactor-host"] = reactor;
root = await component.instantiate(
  async name => WebAssembly.compile(await readFile(join(directory, name))), imports);
assert.equal(root.wasiCliRun, undefined);
handle = root.process.start();
assert.ok(Number.isInteger(handle) && handle > 0);
try {
  assert.equal(root.process.status(handle), 0);
  await completed;
  assert.ok(watchCount > 0 && wakeCount > 0);
  assert.equal(root.process.status(handle), expectedStatus);
  if (expectedStatus === 1) assert.equal(root.process.exitCode(handle), expectedExitCode);
} finally {
  root.process.complete(handle);
  for (const token of pending.keys()) reactor.cancel(token);
}
console.log("Managed component process PASS");
