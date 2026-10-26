import assert from "node:assert/strict";
import test from "node:test";

import { createPageSessionFactory } from "./page-session.mjs";
import { createPageSession } from "./page-session-root.mjs";

const startup = Object.freeze({ buildFingerprint: "1".repeat(64), manifestSha256: "2".repeat(64) });
const configuration = Object.freeze({ arguments: [] });
const platform = Object.freeze({ fetch() {} });
// The page session forwards the entry-selected catalog without reading it.
const platformProviderCatalog = Object.freeze({ providers: Object.freeze([]) });
const snapshotDeploymentManifest = manifest => Object.freeze(manifest);

function fixture({ exports = { Add: (a, b) => a + b }, close = async () => {} } = {}) {
  const calls = [];
  let resolveFailure;
  const failure = new Promise(resolve => { resolveFailure = resolve; });
  const factory = createPageSessionFactory(options => {
    calls.push(["create", options]);
    return async request => {
      calls.push(["open", request]);
      return Object.freeze({
        close: async () => {
          calls.push(["close"]);
          await close();
          resolveFailure(null);
        },
        exports: Object.freeze(exports),
        failure,
      });
    };
  });
  return { calls, factory, resolveFailure };
}

function pageOptions(modules = {}) {
  return {
    configuration,
    manifestUrl: "https://example.test/app/deployment.json",
    modules,
    platform,
    platformProviderCatalog,
    snapshotDeploymentManifest,
  };
}

test("opens one export surface per call with close and failure", async () => {
  const state = fixture();
  const openPage = state.factory(pageOptions());
  assert.equal(Object.isFrozen(openPage), true);

  const first = await openPage({ exports: ["Add"], options: undefined, startup });
  const second = await openPage({ exports: ["Add"], options: {}, startup });

  assert.notEqual(first, second);
  assert.equal(first.Add(2, 3), 5);
  assert.deepEqual(Object.keys(first), ["Add", "close", "failure"]);
  assert.equal(Object.isFrozen(first), true);
  const [, created] = state.calls[0];
  assert.equal(created.configuration, configuration);
  assert.equal(created.platform, platform);
  assert.equal(created.manifestUrl, "https://example.test/app/deployment.json");
  assert.equal(created.platformProviderCatalog, platformProviderCatalog);
  assert.equal(created.snapshotDeploymentManifest, snapshotDeploymentManifest);
  assert.deepEqual(state.calls[1], ["open", { startup }]);
  await first.close();
  assert.equal(await first.failure, null);
});

test("binds bundled and runtime modules and rejects a module supplied twice", async () => {
  const state = fixture();
  const bundled = { report() { return "bundled"; }, version: 3 };
  const runtime = Object.freeze(Object.assign(Object.create(null), { lookup: () => "runtime" }));
  const openPage = state.factory(pageOptions({ "app.host": bundled }));

  await openPage({ exports: ["Add"], options: { modules: { "app.runtime": runtime } }, startup });
  const modules = state.calls[0][1].consumerModules;
  assert.deepEqual(Object.keys(modules), ["app.host", "app.runtime"]);
  assert.deepEqual(Object.keys(modules["app.host"]), ["report"]);
  assert.equal(modules["app.runtime"].lookup(), "runtime");
  assert.equal(Object.isFrozen(modules["app.host"]), true);

  await assert.rejects(() => openPage({
    exports: ["Add"], options: { modules: { "app.host": runtime } }, startup,
  }), /bundled and also supplied/);
  await assert.rejects(() => openPage({
    exports: ["Add"], options: { modules: { "app.other": 7 } }, startup,
  }), /must be a module namespace/);
});

test("delivers worker-style notifications after the import returns", async () => {
  const state = fixture();
  const delivered = [];
  const failures = [];
  const originalReportError = globalThis.reportError;
  globalThis.reportError = cause => failures.push(cause.message);
  try {
    const openPage = state.factory(pageOptions({
      "app.host": {
        createWorkerImports(notify) {
          return {
            invalid: () => notify("", []),
            progress: (done, total) => notify("progress", [done, total]),
          };
        },
      },
    }));
    const page = await openPage({ exports: ["Add"], options: {
      onNotification: value => {
        delivered.push(value);
        if (value.arguments[0] === 2) throw new Error("handler failed");
        if (value.arguments[0] === 3) return Promise.reject(new Error("handler rejected"));
        return undefined;
      },
    }, startup });
    const { progress } = state.calls[0][1].consumerModules["app.host"];
    progress(1, 4);
    assert.deepEqual(delivered, []);
    await Promise.resolve();
    assert.deepEqual(delivered, [{ operation: "progress", arguments: [1, 4] }]);
    assert.equal(Object.isFrozen(delivered[0].arguments), true);
    progress(2, 4);
    progress(3, 4);
    await new Promise(resolve => setTimeout(resolve, 0));
    assert.deepEqual(failures, ["handler failed", "handler rejected"]);
    assert.throws(() => state.calls[0][1].consumerModules["app.host"].invalid(), /name and argument/);

    await page.close();
    progress(4, 4);
    await new Promise(resolve => setTimeout(resolve, 0));
    assert.equal(delivered.length, 3);
  } finally {
    globalThis.reportError = originalReportError;
  }
});

test("reports notification failures to the console without reportError", async () => {
  const state = fixture();
  const originalReportError = globalThis.reportError;
  const originalError = console.error;
  const logged = [];
  globalThis.reportError = undefined;
  console.error = cause => logged.push(cause.message);
  try {
    const openPage = state.factory(pageOptions({
      "app.host": { createWorkerImports: notify => ({ ping: () => notify("ping") }) },
    }));
    await openPage({ exports: ["Add"], options: {
      onNotification: () => { throw new Error("console failure"); },
    }, startup });
    state.calls[0][1].consumerModules["app.host"].ping();
    await Promise.resolve();
    assert.deepEqual(logged, ["console failure"]);
  } finally {
    globalThis.reportError = originalReportError;
    console.error = originalError;
  }
});

test("ignores notifications without a handler and stops after a failure", async () => {
  const state = fixture();
  let notify;
  const openPage = state.factory(pageOptions({
    "app.host": { createWorkerImports: value => { notify = value; return { ping: () => {} }; } },
  }));
  await openPage({ exports: ["Add"], options: {}, startup });
  notify("ping", []);

  const delivered = [];
  await openPage({ exports: ["Add"], options: { onNotification: value => delivered.push(value) }, startup });
  state.resolveFailure(new Error("trap"));
  await Promise.resolve();
  notify("ping", []);
  await new Promise(resolve => setTimeout(resolve, 0));
  assert.deepEqual(delivered, []);
});

test("delivers output one line at a time and flushes the last line when the session ends", async () => {
  const state = fixture();
  const openPage = state.factory(pageOptions());
  const lines = [];
  const page = await openPage({ exports: ["Add"], options: { stdout: line => lines.push(line) }, startup });
  const { stdout } = state.calls[0][1];
  const bytes = new TextEncoder().encode("first\r\nsecénd\nthird");
  stdout.write(bytes.subarray(0, 9));
  stdout.write(bytes.subarray(9, 13));
  assert.deepEqual(lines, ["first"]);
  stdout.write(bytes.subarray(13));
  assert.deepEqual(lines, ["first", "secénd"]);

  await page.close();
  await Promise.resolve();
  assert.deepEqual(lines, ["first", "secénd", "third"]);
});

test("writes to the console by default without empty trailing lines", async () => {
  const state = fixture();
  const page = await state.factory(pageOptions())({ exports: ["Add"], options: {}, startup });
  const logged = [];
  const originalLog = console.log;
  const originalError = console.error;
  console.log = text => logged.push(["log", text]);
  console.error = text => logged.push(["error", text]);
  try {
    state.calls[0][1].stdout.write(new TextEncoder().encode("out\n"));
    state.calls[0][1].stderr.write(new TextEncoder().encode("partial"));
    await page.close();
    await Promise.resolve();
  } finally {
    console.log = originalLog;
    console.error = originalError;
  }
  assert.deepEqual(logged, [["log", "out"], ["error", "partial"]]);
});

test("merges open() environment values over the build's and grants each name", async () => {
  const state = fixture();
  const configured = Object.freeze({
    arguments: Object.freeze([]),
    environment: Object.freeze([
      Object.freeze({ name: "MODE", value: "build" }),
      Object.freeze({ name: "KEEP", value: "1" }),
    ]),
    grants: Object.freeze({ clocks: Object.freeze(["wall"]), environment: Object.freeze(["MODE", "KEEP"]) }),
  });
  const openPage = state.factory({ ...pageOptions(), configuration: configured });

  await openPage({ exports: ["Add"], options: { environment: { MODE: "page", API_BASE: "/api" } }, startup });
  await openPage({ exports: ["Add"], options: {}, startup });

  const merged = state.calls[0][1].configuration;
  assert.deepEqual(merged.environment, [
    { name: "MODE", value: "page" },
    { name: "KEEP", value: "1" },
    { name: "API_BASE", value: "/api" },
  ]);
  assert.deepEqual(merged.grants, { clocks: ["wall"], environment: ["MODE", "KEEP", "API_BASE"] });
  assert.equal(Object.isFrozen(merged.grants.environment), true);
  assert.equal(state.calls[2][1].configuration, configured);
});

test("platform hooks replace the wall clock and every random source", async () => {
  const state = fixture();
  const shimConfigurations = [];
  const original = {
    "wasi:clocks/monotonic-clock@0.2.11": Object.freeze({ now: () => 5n }),
    "wasi:clocks/wall-clock@0.2.11": Object.freeze({ now: () => assert.fail("real clock"), resolution: () => "ms" }),
    "wasi:random/insecure-seed@0.2.11": Object.freeze({ insecureSeed: () => assert.fail("real seed") }),
    "wasi:random/insecure@0.2.11": Object.freeze({
      getInsecureRandomBytes: () => assert.fail("real bytes"), getInsecureRandomU64: () => assert.fail("real u64"),
    }),
    "wasi:random/random@0.2.11": Object.freeze({
      getRandomBytes: () => assert.fail("real bytes"), getRandomU64: () => assert.fail("real u64"),
    }),
  };
  const openPage = state.factory({
    ...pageOptions(),
    platform: Object.freeze({
      fetch() {},
      createShim(configuration) {
        shimConfigurations.push(configuration);
        return { getImportObject: request => (request.asVersion === "0.2.11" ? original : null) };
      },
    }),
  });
  let counter = 0;
  await openPage({ exports: ["Add"], options: { platform: {
    now: () => 1_700_000_000_123.5,
    random: bytes => { for (let index = 0; index < bytes.length; index++) bytes[index] = ++counter; },
  } }, startup });

  const { platform: hooked } = state.calls[0][1];
  assert.equal(hooked.fetch, state.calls[0][1].platform.fetch);
  const imports = hooked.createShim("config").getImportObject({ asVersion: "0.2.11" });
  assert.deepEqual(shimConfigurations, ["config"]);
  assert.deepEqual(imports["wasi:clocks/wall-clock@0.2.11"].now(), { seconds: 1_700_000_000n, nanoseconds: 123_500_000 });
  assert.equal(imports["wasi:clocks/wall-clock@0.2.11"].resolution(), "ms");
  assert.equal(imports["wasi:clocks/monotonic-clock@0.2.11"].now(), 5n);
  assert.deepEqual([...imports["wasi:random/random@0.2.11"].getRandomBytes(3n)], [1, 2, 3]);
  assert.equal(imports["wasi:random/random@0.2.11"].getRandomU64(), 0x0b0a090807060504n);
  assert.deepEqual([...imports["wasi:random/insecure@0.2.11"].getInsecureRandomBytes(1n)], [12]);
  assert.equal(typeof imports["wasi:random/insecure@0.2.11"].getInsecureRandomU64(), "bigint");
  assert.equal(imports["wasi:random/insecure-seed@0.2.11"].insecureSeed().length, 2);
});

test("platform hooks leave modules the shim does not supply and reject invalid clock values", async () => {
  const state = fixture();
  const openPage = state.factory({
    ...pageOptions(),
    platform: Object.freeze({ createShim: () => ({ getImportObject: () => ({ "wasi:cli/environment@0.2.11": {} }) }) }),
  });
  await openPage({ exports: ["Add"], options: { platform: { now: () => 0, random: () => {} } }, startup });
  const imports = state.calls[0][1].platform.createShim().getImportObject();
  assert.deepEqual(Object.keys(imports), ["wasi:cli/environment@0.2.11"]);

  const clocks = fixture();
  const clockPage = clocks.factory({
    ...pageOptions(),
    platform: Object.freeze({ createShim: () => ({ getImportObject: () => ({
      "wasi:clocks/wall-clock@0.2.11": { now() {} },
    }) }) }),
  });
  for (const value of [-1, Number.NaN, "now"]) {
    await clockPage({ exports: ["Add"], options: { platform: { now: () => value } }, startup });
    const wallClock = clocks.calls.at(-2)[1].platform.createShim().getImportObject()["wasi:clocks/wall-clock@0.2.11"];
    assert.throws(() => wallClock.now(), /non-negative epoch milliseconds/);
  }
});

test("closes and rejects a deployment whose exports differ from the generated module", async () => {
  const state = fixture({ exports: { Add() {}, Extra() {} } });
  const openPage = state.factory(pageOptions());
  await assert.rejects(() => openPage({ exports: ["Add"], options: {}, startup }),
    /do not match the generated module/);
  assert.deepEqual(state.calls.at(-1), ["close"]);

  const renamed = fixture({ exports: { Sub() {} } });
  await assert.rejects(() => renamed.factory(pageOptions())({ exports: ["Add"], options: {}, startup }),
    /do not match/);

  const failing = fixture({
    exports: { Other() {} },
    close: async () => { throw new Error("cleanup failed"); },
  });
  await assert.rejects(() => failing.factory(pageOptions())({ exports: ["Add"], options: {}, startup }),
    error => error instanceof AggregateError && error.errors[1].message === "cleanup failed");
});

test("validates the factory, request, export names and open options", async () => {
  assert.throws(() => createPageSessionFactory(null), /export-session factory/);
  const state = fixture();
  assert.throws(() => state.factory({ ...pageOptions(), extra: true }), /shape is invalid/);
  assert.throws(() => state.factory({ ...pageOptions(), modules: null }), /module map is invalid/);
  assert.throws(() => state.factory(Object.defineProperty(pageOptions(), "modules", {
    enumerable: false, value: {},
  })), /plain data object/);
  const openPage = state.factory(pageOptions());

  for (const exports of [null, [""], ["close"], ["then"], [7], ["Add", "Add"]]) {
    await assert.rejects(() => openPage({ exports, options: {}, startup }), /export names/);
  }
  for (const [options, pattern] of [
    [null, /open options is invalid/],
    [{ extra: true }, /'extra' is unsupported/],
    [{ onNotification: 1 }, /onNotification must be a function/],
    [{ stdout: null }, /stdout must be a function/],
    [{ stderr: { write() {} } }, /stderr must be a function/],
    [{ environment: null }, /environment must be a plain object/],
    [{ environment: { MODE: 1 } }, /environment values must be strings/],
    [{ platform: [] }, /platform hooks is invalid/],
    [{ platform: { clock: () => 0 } }, /hook 'clock' is unsupported/],
    [{ platform: { now: 0 } }, /hook 'now' is unsupported/],
  ]) {
    await assert.rejects(() => openPage({ exports: ["Add"], options, startup }), pattern);
  }
  await assert.rejects(() => openPage({ exports: ["Add"], startup }), /request shape is invalid/);
});

test("the root wires the browser raw export session", async () => {
  const openPage = createPageSession(pageOptions());
  await assert.rejects(() => openPage({ exports: ["Add"], options: {}, startup }),
    /browser raw export-session configuration shape is invalid/);
});
