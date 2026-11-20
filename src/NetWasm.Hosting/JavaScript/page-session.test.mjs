// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: MIT

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

test("platform hooks replace the system clock and every random source", async () => {
  const state = fixture();
  const requests = [];
  const release = async () => {};
  const original = {
    "wasi:clocks/monotonic-clock@0.3.2": Object.freeze({ now: () => 5n }),
    "wasi:clocks/system-clock@0.3.2": Object.freeze({ now: () => assert.fail("real clock"), getResolution: () => 1000n }),
    "wasi:random/insecure-seed@0.3.2": Object.freeze({ getInsecureSeed: () => assert.fail("real seed") }),
    "wasi:random/insecure@0.3.2": Object.freeze({
      getInsecureRandomBytes: () => assert.fail("real bytes"), getInsecureRandomU64: () => assert.fail("real u64"),
    }),
    "wasi:random/random@0.3.2": Object.freeze({
      getRandomBytes: () => assert.fail("real bytes"), getRandomU64: () => assert.fail("real u64"),
    }),
  };
  const openPage = state.factory({
    ...pageOptions(),
    platform: Object.freeze({
      fetch() {},
      createImports(request) {
        requests.push(request);
        return Object.freeze({ imports: original, release });
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
  const importSet = hooked.createImports("request");
  assert.deepEqual(requests, ["request"]);
  assert.equal(importSet.release, release);
  const { imports } = importSet;
  assert.deepEqual(imports["wasi:clocks/system-clock@0.3.2"].now(), { seconds: 1_700_000_000n, nanoseconds: 123_500_000 });
  assert.equal(imports["wasi:clocks/system-clock@0.3.2"].getResolution(), 1000n);
  assert.equal(imports["wasi:clocks/monotonic-clock@0.3.2"].now(), 5n);
  assert.deepEqual([...imports["wasi:random/random@0.3.2"].getRandomBytes(3n)], [1, 2, 3]);
  assert.equal(imports["wasi:random/random@0.3.2"].getRandomU64(), 0x0b0a090807060504n);
  assert.deepEqual([...imports["wasi:random/insecure@0.3.2"].getInsecureRandomBytes(1n)], [12]);
  assert.equal(typeof imports["wasi:random/insecure@0.3.2"].getInsecureRandomU64(), "bigint");
  assert.equal(imports["wasi:random/insecure-seed@0.3.2"].getInsecureSeed().length, 2);
});

test("platform hooks leave modules the platform does not supply and reject invalid clock values", async () => {
  const importSet = imports => () => Object.freeze({ imports, release: async () => {} });
  const state = fixture();
  const openPage = state.factory({
    ...pageOptions(),
    platform: Object.freeze({ createImports: importSet({ "wasi:cli/environment@0.3.2": {} }) }),
  });
  await openPage({ exports: ["Add"], options: { platform: { now: () => 0, random: () => {} } }, startup });
  const { imports } = state.calls[0][1].platform.createImports();
  assert.deepEqual(Object.keys(imports), ["wasi:cli/environment@0.3.2"]);

  const clocks = fixture();
  const clockPage = clocks.factory({
    ...pageOptions(),
    platform: Object.freeze({ createImports: importSet({ "wasi:clocks/system-clock@0.3.2": { now() {} } }) }),
  });
  for (const value of [-1, Number.NaN, "now"]) {
    await clockPage({ exports: ["Add"], options: { platform: { now: () => value } }, startup });
    const systemClock = clocks.calls.at(-2)[1].platform.createImports().imports["wasi:clocks/system-clock@0.3.2"];
    assert.throws(() => systemClock.now(), /non-negative epoch milliseconds/);
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

test("prefetched responses go to the export session's fetch once each", async () => {
  const state = fixture();
  const fetched = [];
  const openPage = state.factory({ ...pageOptions(), platform: Object.freeze({
    fetch: (input, init) => {
      fetched.push(input);
      return Promise.resolve("fetched");
    },
  }) });
  const url = "https://example.test/app/App.wasm";
  const response = Promise.resolve("prefetched");
  const prefetched = new Map([[url, response]]);

  await openPage({ exports: ["Add"], options: undefined, prefetched, startup });
  await openPage({ exports: ["Add"], options: undefined, prefetched, startup });
  const first = state.calls[0][1].platform;
  const second = state.calls[2][1].platform;

  assert.equal(Object.isFrozen(first), true);
  assert.equal(first.fetch(url), response);
  assert.equal(await second.fetch(url), "fetched");
  assert.deepEqual(fetched, [url]);
  await assert.rejects(() => openPage({ exports: ["Add"], options: undefined, prefetched: {}, startup }),
    /prefetched responses must be a Map/);
  await assert.rejects(() => openPage({ exports: ["Add"], prefetched, startup }), /request shape is invalid/);
  await assert.rejects(() => openPage(null), /open request is invalid/);
});

test("lets the collector use the page's next idle period whenever the module ran", async () => {
  const idle = [];
  let collections = 0;
  const session = collectWhenIdle => Object.freeze({
    close: async () => {},
    ...(collectWhenIdle === undefined ? {} : { collectWhenIdle }),
    exports: Object.freeze({ Add: (a, b) => a + b, version: 3 }),
    failure: new Promise(() => {}),
  });
  let imports;
  let importCalled;
  const factory = collectWhenIdle => createPageSessionFactory(options => {
    imports = options.consumerModules;
    importCalled = options.observeImportCall;
    return async () => session(collectWhenIdle);
  }, run => idle.push(run));
  const host = { read() { return this === undefined ? "detached" : "bound"; }, label: "page" };
  const openPage = factory(() => { collections++; return true; })(pageOptions({ host }));

  // Opening ran the module, so does each export call and each import the module calls.
  const page = await openPage({ exports: ["Add", "version"], options: undefined, startup });
  assert.equal(idle.length, 1);
  assert.equal(page.Add(2, 3), 5);
  assert.equal(page.version, 3);
  assert.equal(imports.host.read(), "bound");
  assert.equal(idle.length, 1);
  idle.shift()();
  assert.equal(collections, 1);
  // The modules stay as written; the export session reports each import the program calls.
  assert.equal(imports.host.read, host.read);
  assert.equal(imports.host.read.call(undefined), "detached");
  assert.equal(idle.length, 0);
  importCalled();
  assert.equal(idle.length, 1);
  page.Add(1, 1);
  assert.equal(idle.length, 1);

  // A runtime without the idle entry, and a host that never says it is idle, are left alone.
  const before = idle.length;
  const plain = await factory(undefined)(pageOptions())({ exports: ["Add", "version"], options: undefined, startup });
  plain.Add(1, 1);
  const quiet = await createPageSessionFactory(() => async () => session(() => true))(pageOptions())(
    { exports: ["Add", "version"], options: undefined, startup });
  quiet.Add(1, 1);
  assert.equal(idle.length, before);
  assert.throws(() => createPageSessionFactory(() => {}, "idle"), /idle scheduler must be a function/);
});

test("passes a module's exported classes as written, so a dotted import reaches a static member", async () => {
  class AvaloniaDOM {
    static #created = 0;
    static addClass(element, name) { element.classes.push(name); return AvaloniaDOM.#created; }
    constructor() { AvaloniaDOM.#created++; }
  }
  const avalonia = Object.freeze({ AvaloniaDOM, version: 1 });
  let modules;
  const open = createPageSessionFactory(options => {
    modules = options.consumerModules;
    return async () => Object.freeze({
      close: async () => {}, exports: Object.freeze({}), failure: new Promise(() => {}),
    });
  })(pageOptions({ avalonia }));

  await open({ exports: [], options: undefined, startup });

  assert.equal(modules.avalonia.AvaloniaDOM, AvaloniaDOM);
  const element = { classes: [] };
  new modules.avalonia.AvaloniaDOM();
  assert.equal(modules.avalonia.AvaloniaDOM.addClass.call(modules.avalonia.AvaloniaDOM, element, "root"), 1);
  assert.deepEqual(element.classes, ["root"]);
});

test("passes a module's object members, so a dotted import reaches a function an object holds", async () => {
  const Format = Object.freeze({ prefix: "fmt:", wrap(value) { return this.prefix + value; } });
  const formats = Object.freeze({ Format, stamp: value => `stamp:${value}`, version: 1 });
  let modules;
  const open = createPageSessionFactory(options => {
    modules = options.consumerModules;
    return async () => Object.freeze({
      close: async () => {}, exports: Object.freeze({}), failure: new Promise(() => {}),
    });
  })(pageOptions({ formats }));

  await open({ exports: [], options: undefined, startup });

  assert.equal(modules.formats.Format, Format);
  assert.equal(modules.formats.Format.wrap.call(modules.formats.Format, "x"), "fmt:x");
  assert.equal(modules.formats.stamp("x"), "stamp:x");
  assert.equal(Object.hasOwn(modules.formats, "version"), false);
});

test("lets the collector collect once the module has stayed quiet, unless an idle collection came first", async () => {
  const idle = [];
  const timers = [];
  let idleAnswer = false;
  let quiet = 0;
  const session = collectors => Object.freeze({
    close: async () => {},
    ...collectors,
    exports: Object.freeze({ Add: (a, b) => a + b }),
    failure: new Promise(() => {}),
  });
  const timer = { now: () => 0, setTimer: (run, milliseconds) => timers.push({ milliseconds, run }) };
  const open = (collectors, requestIdle = run => idle.push(run), quietTimer = timer) =>
    createPageSessionFactory(() => async () => session(collectors), requestIdle, quietTimer)(pageOptions())(
      { exports: ["Add"], options: undefined, startup });
  const collectors = { collectWhenIdle: () => idleAnswer, collectWhenQuiet: () => { quiet++; return 256; } };

  // Opening ran the module: the timer is set once, and calls during its period set no other.
  const page = await open(collectors);
  assert.deepEqual(timers.map(entry => entry.milliseconds), [1000]);
  page.Add(1, 2);
  assert.equal(timers.length, 1);
  // The module ran during the period, so it runs out another, and then the collection follows.
  timers.shift().run();
  assert.equal(quiet, 0);
  timers.shift().run();
  assert.equal(quiet, 1);

  // An idle collection that declined leaves the quiet one due; one that collected leaves it nothing to do.
  page.Add(1, 2);
  idle.shift()();
  timers.shift().run();
  assert.equal(quiet, 2);
  page.Add(1, 2);
  idleAnswer = true;
  idle.shift()();
  timers.shift().run();
  assert.equal(quiet, 2);

  // A host without an idle scheduler still collects when quiet.
  const timed = await open(collectors, null);
  assert.equal(timers.length, 1);
  timers.shift().run();
  assert.equal(quiet, 3);
  timed.Add(1, 2);
  timers.shift().run();
  assert.equal(quiet, 4);

  // A runtime without the quiet entry, and a host without a timer, are left alone.
  await open({ collectWhenIdle: () => true });
  await open(collectors, null, null);
  assert.equal(timers.length, 0);
  for (const invalid of ["timer", { now() {} }, { setTimer() {} }, { now: 1, setTimer() {} }]) {
    assert.throws(() => createPageSessionFactory(() => {}, null, invalid), /quiet timer must be a timer and a clock/);
  }
});

test("the root wires the browser raw export session", async () => {
  const openPage = createPageSession(pageOptions());
  await assert.rejects(() => openPage({ exports: ["Add"], options: {}, startup }),
    /browser raw export-session configuration shape is invalid/);
});
