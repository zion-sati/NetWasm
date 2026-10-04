import assert from "node:assert/strict";
import test from "node:test";
import { NetWasmHostError } from "./managed-errors.mjs";
import { createRawExportSessionOpener } from "./raw-export-session-opener.mjs";
import { createScopeReleaseCommand } from "./scope-release-command.mjs";

const reactorModule = "cm32p2|netwasm:runtime/reactor-host@1";
const reactorGuest = "cm32p2|netwasm:runtime/reactor-guest@1|wake";
const initializeExport = "cm32p2_initialize";

function adapter(withReactor = false) {
  return {
    rawAdapterMetadata: {
      requiredCapabilities: withReactor ? ["bindReactor"] : [],
    },
  };
}

function fixture({
  failAt = null,
  instanceOverride = null,
  onInteropClose = null,
  onInitialize = null,
  pendingExport = false,
  planOverrides = {},
  probeExportAtPrepare = false,
  probeReactorAtCanonical = false,
  reactor = false,
  reactorReadyBeforeBind = false,
  reactorProduct,
  releaseCommandFactory = createScopeReleaseCommand,
  releaseFailures = [],
} = {}) {
  const calls = [];
  const controls = {};
  const fail = name => {
    if (failAt === name) throw new Error(`${name} failed`);
  };
  const dependencies = {
    bindCanonicalInstance() {
      calls.push("bind-canonical");
      fail("bind-canonical");
    },
    buildPlan() {
      calls.push("plan");
      fail("plan");
      return {
        target: "wasm32",
        imports: [],
        reactorHostModule: reactorModule,
        reactorGuestExport: reactorGuest,
        canonicalInitializeExport: initializeExport,
        ...planOverrides,
      };
    },
    closeCanonicalBinding() {
      calls.push("close-canonical");
      if (releaseFailures.includes("canonical")) throw new Error("canonical cleanup failed");
    },
    composeImports() {
      calls.push("compose");
      fail("compose");
      return Object.freeze(Object.create(null));
    },
    createCanonicalBinding(request) {
      calls.push("canonical");
      controls.canonicalRequest = request;
      if (probeReactorAtCanonical && request.reactor !== null) {
        try { request.reactor.assertAvailable(); } catch (error) { controls.beforeReadyError = error; }
      }
      fail("canonical");
      return Object.freeze({ imports: Object.freeze({}), target: "wasm32" });
    },
    createGuestNotifier(request) {
      calls.push("notifier");
      controls.notifierRequest = request;
      fail("notifier");
      return Object.freeze({
        notify(token) {
          calls.push(`notify:${token}`);
          try {
            request.guestWake(token);
            request.observeWake();
          } catch {
            request.observeWake(Object.freeze({}));
          }
        },
      });
    },
    createReactor(request) {
      calls.push("reactor");
      controls.reactorRequest = request;
      fail("reactor");
      if (reactorProduct !== undefined) return reactorProduct;
      if (reactorReadyBeforeBind) request.onReady(3);
      return {
        watch() { calls.push("reactor-watch"); },
        cancel() { calls.push("reactor-cancel"); },
        close() {
          calls.push("close-reactor");
          if (releaseFailures.includes("reactor")) throw new Error("reactor cleanup failed");
        },
      };
    },
    createReleaseCommand: releaseCommandFactory,
  };
  const instance = instanceOverride ?? {
    exports: {
      marker: 37,
      [initializeExport]() {
        calls.push(`initialize:${this.marker}`);
        onInitialize?.(controls);
        fail("initialize");
      },
      [reactorGuest](token) {
        calls.push(`wake:${this.marker}:${token}`);
        fail("wake");
      },
    },
  };
  const request = {
    abi: {},
    adapter: adapter(reactor),
    instantiate: async value => {
      calls.push("instantiate");
      controls.instantiateRequest = value;
      fail("instantiate");
      return failAt === "wrapped-instance" ? { instance } : instance;
    },
    manifest: {},
    module: {},
    prepareInterop(lifetime) {
      calls.push("prepare-interop");
      controls.lifetime = lifetime;
      if (probeExportAtPrepare) {
        try { lifetime.assertAvailable(); } catch (error) { controls.beforeReadyExportError = error; }
      }
      fail("prepare-interop");
      return {
        consumeTerminalEvent() {},
        async drainTerminalReports() {},
        imports: {},
        bindInstance(value) {
          calls.push("bind-interop");
          assert.equal(value, instance);
          fail("bind-interop");
          const exports = {
              read() {
                lifetime.assertAvailable();
                calls.push("read");
                return 42;
              },
          };
          if (pendingExport) {
            exports.pending = () => {
              lifetime.assertAvailable();
              return new Promise((_, reject) => { controls.rejectPending = reject; });
            };
          }
          return {
            exports: Object.freeze(exports),
          };
        },
        close() {
          calls.push("close-interop");
          controls.rejectPending?.(new NetWasmHostError("pending export closed"));
          onInteropClose?.(controls);
          if (releaseFailures.includes("interop")) throw new Error("interop cleanup failed");
        },
      };
    },
    providers: {},
    releaseActions: [() => {
      calls.push("close-caller");
      if (releaseFailures.includes("caller")) throw new Error("caller cleanup failed");
    }],
    schedule: callback => callback(),
  };
  return {
    calls,
    controls,
    instance,
    opener: createRawExportSessionOpener(dependencies),
    request,
  };
}

test("opens, initializes and closes a non-reactor export session exactly once", async () => {
  const state = fixture();
  const session = await state.opener.open(state.request);

  assert.equal(Object.isFrozen(state.opener), true);
  assert.equal(Object.isFrozen(session), true);
  assert.equal(session.exports.read(), 42);
  assert.deepEqual(state.calls, [
    "plan", "prepare-interop", "canonical", "compose", "instantiate",
    "bind-interop", "bind-canonical", "initialize:37", "read",
  ]);
  assert.equal(state.controls.canonicalRequest.reactor, null);
  assert.doesNotThrow(() => state.controls.lifetime.assertAsyncDeliveryAvailable());
  assert.equal(Object.isFrozen(state.controls.instantiateRequest), true);
  const firstClose = session.close();
  const secondClose = session.close();
  assert.equal(firstClose, secondClose);
  await firstClose;
  assert.equal(await session.failure, null);
  assert.deepEqual(state.calls.slice(-3), [
    "close-interop", "close-canonical", "close-caller",
  ]);
  assert.throws(() => session.exports.read(), /closed/);
  assert.throws(() => state.controls.lifetime.assertAsyncDeliveryAvailable(), /closed/);
});

test("binds a declared reactor before initialization and reports readiness", async () => {
  const state = fixture({ reactor: true });
  const session = await state.opener.open(state.request);
  assert.deepEqual(state.calls.slice(0, 9), [
    "plan", "prepare-interop", "reactor", "canonical", "compose", "instantiate",
    "bind-interop", "bind-canonical", "notifier",
  ]);
  assert.equal(state.controls.canonicalRequest.reactor.module, reactorModule);
  assert.doesNotThrow(() => state.controls.canonicalRequest.reactor.assertAvailable());
  state.controls.reactorRequest.onReady(11);
  assert.deepEqual(state.calls.slice(-2), ["notify:11", "wake:37:11"]);
  await session.close();
  assert.deepEqual(state.calls.slice(-4), [
    "close-interop", "close-reactor", "close-canonical", "close-caller",
  ]);
});

test("makes the first asynchronous or reactor failure permanent", async () => {
  for (const source of ["interop", "reactor", "wake"]) {
    const state = fixture({
      reactor: source !== "interop",
      failAt: source === "wake" ? "wake" : null,
      pendingExport: true,
    });
    const session = await state.opener.open(state.request);
    const pending = session.exports.pending();
    const cause = new Error("private failure");
    if (source === "interop") state.controls.lifetime.observeAsyncFailure(cause);
    else if (source === "reactor") state.controls.reactorRequest.onFailure();
    else state.controls.reactorRequest.onReady(7);
    const failure = await session.failure;
    assert.equal(failure instanceof NetWasmHostError, true);
    assert.throws(() => session.exports.read(), error => error === failure);
    assert.throws(() => state.controls.lifetime.assertAsyncDeliveryAvailable(),
      error => error === failure);
    if (source !== "interop") {
      assert.throws(() => state.controls.canonicalRequest.reactor.assertAvailable(),
        error => error === failure);
      const callCount = state.calls.length;
      state.controls.reactorRequest.onReady(9);
      assert.equal(state.calls.length, callCount);
    }
    state.controls.lifetime.observeAsyncFailure(new Error("later"));
    assert.equal(await session.failure, failure);
    await assert.rejects(pending, /closed/);
    await new Promise(resolve => setTimeout(resolve, 0));
    assert.deepEqual(state.calls.filter(value => value.startsWith("close-")),
      source === "interop"
        ? ["close-interop", "close-canonical", "close-caller"]
        : ["close-interop", "close-reactor", "close-canonical", "close-caller"]);
    await session.close();
  }
});

test("rolls back if a reactor becomes ready before guest binding", async () => {
  const state = fixture({ reactor: true, reactorReadyBeforeBind: true });
  await assert.rejects(() => state.opener.open(state.request), /before binding/);
  assert.equal(state.calls.some(value => value.startsWith("initialize:")), false);
  assert.deepEqual(state.calls.slice(-3), [
    "close-interop", "close-reactor", "close-caller",
  ]);
});

test("does not initialize after a terminal failure arrives during instantiation", async () => {
  const state = fixture();
  state.request.instantiate = async value => {
    state.calls.push("instantiate");
    state.controls.instantiateRequest = value;
    await Promise.resolve();
    state.controls.lifetime.observeAsyncFailure(new Error("failed while instantiating"));
    return state.instance;
  };

  await assert.rejects(() => state.opener.open(state.request), /session failed/);
  assert.equal(state.calls.some(value => value.startsWith("initialize:")), false);
  assert.deepEqual(state.calls.filter(value => value.startsWith("close-")), [
    "close-interop", "close-canonical", "close-caller",
  ]);
});

test("allows reactor use during initialization but rejects it before and after", async () => {
  let initializationAvailable = false;
  const state = fixture({
    reactor: true,
    probeReactorAtCanonical: true,
    onInitialize(controls) {
      controls.canonicalRequest.reactor.assertAvailable();
      initializationAvailable = true;
    },
  });
  const session = await state.opener.open(state.request);
  assert.equal(initializationAvailable, true);
  assert.equal(state.controls.beforeReadyError instanceof NetWasmHostError, true);
  await session.close();
  assert.throws(() => state.controls.canonicalRequest.reactor.assertAvailable(), /unavailable/);
});

test("rejects export access before readiness and uses the platform scheduler default", async () => {
  const state = fixture({ probeExportAtPrepare: true });
  delete state.request.schedule;
  const session = await state.opener.open(state.request);
  assert.match(state.controls.beforeReadyExportError.message, /not ready/);
  await session.close();

  const originalQueueMicrotask = globalThis.queueMicrotask;
  try {
    globalThis.queueMicrotask = undefined;
    const missing = fixture();
    delete missing.request.schedule;
    await assert.rejects(() => missing.opener.open(missing.request), /actions/);
  } finally {
    globalThis.queueMicrotask = originalQueueMicrotask;
  }
});

test("rolls back every acquired scope for each startup failure", async () => {
  const cases = [
    ["plan", ["close-caller"]],
    ["prepare-interop", ["close-caller"]],
    ["reactor", ["close-interop", "close-caller"]],
    ["canonical", ["close-interop", "close-reactor", "close-caller"]],
    ["compose", ["close-interop", "close-reactor", "close-canonical", "close-caller"]],
    ["instantiate", ["close-interop", "close-reactor", "close-canonical", "close-caller"]],
    ["bind-interop", ["close-interop", "close-reactor", "close-canonical", "close-caller"]],
    ["bind-canonical", ["close-interop", "close-reactor", "close-canonical", "close-caller"]],
    ["notifier", ["close-interop", "close-reactor", "close-canonical", "close-caller"]],
    ["initialize", ["close-interop", "close-reactor", "close-canonical", "close-caller"]],
  ];
  for (const [failAt, expectedClosing] of cases) {
    const state = fixture({ failAt, reactor: !["plan", "prepare-interop"].includes(failAt) });
    await assert.rejects(() => state.opener.open(state.request), new RegExp(failAt));
    assert.deepEqual(state.calls.filter(value => value.startsWith("close-")), expectedClosing);
  }
});

test("attempts all cleanup and reports close and rollback failures", async () => {
  const closing = fixture({
    reactor: true,
    releaseFailures: ["interop", "reactor", "canonical", "caller"],
  });
  const session = await closing.opener.open(closing.request);
  await assert.rejects(() => session.close(), error =>
    error instanceof AggregateError && error.errors.length === 4);
  assert.equal((await session.failure) instanceof AggregateError, true);
  assert.deepEqual(closing.calls.slice(-4), [
    "close-interop", "close-reactor", "close-canonical", "close-caller",
  ]);

  const rollback = fixture({
    failAt: "initialize",
    reactor: true,
    releaseFailures: ["interop", "canonical"],
  });
  await assert.rejects(() => rollback.opener.open(rollback.request), error =>
    error instanceof AggregateError && error.errors.length === 3);
  assert.deepEqual(rollback.calls.slice(-4), [
    "close-interop", "close-reactor", "close-canonical", "close-caller",
  ]);

  const terminal = fixture({ releaseFailures: ["interop"] });
  const terminalSession = await terminal.opener.open(terminal.request);
  terminal.controls.lifetime.observeAsyncFailure(new Error("terminal"));
  const firstFailure = await terminalSession.failure;
  await new Promise(resolve => setTimeout(resolve, 0));
  assert.equal(await terminalSession.failure, firstFailure);
  await assert.rejects(() => terminalSession.close(), AggregateError);

  const rejectedRelease = new Error("release command rejected");
  const rejecting = fixture({
    releaseCommandFactory: () => ({ close: () => Promise.reject(rejectedRelease) }),
  });
  const rejectingSession = await rejecting.opener.open(rejecting.request);
  await assert.rejects(() => rejectingSession.close(), error => error === rejectedRelease);
  assert.equal(await rejectingSession.failure, rejectedRelease);
});

test("installs the session completion before cleanup can reenter close", async () => {
  let session;
  let nestedClose;
  const state = fixture({
    onInteropClose() { nestedClose = session.close(); },
  });
  session = await state.opener.open(state.request);

  const firstClose = session.close();
  await Promise.resolve();
  assert.equal(nestedClose, firstClose);
  await firstClose;
  assert.equal(state.calls.filter(value => value === "close-interop").length, 1);
  assert.equal(session.close(), firstClose);
});

test("accepts a wrapped instance and rejects invalid instances, boundaries and exports", async () => {
  const wrapped = fixture({ failAt: "wrapped-instance" });
  const session = await wrapped.opener.open(wrapped.request);
  await session.close();

  const bytes = Buffer.from(
    "AGFzbQEAAAABBQFgAAF/AhsBD25ldHdhc20uaG9zdC52MQdzZXJ2aWNlAAADAgEABQMBAAEHEAIGbWVtb3J5AgADcnVuAAEKBgEEABAACwAdBG5hbWUBCgEAB3NlcnZpY2UECgEAB3NlcnZpY2U=",
    "base64");
  const module = new WebAssembly.Module(bytes);
  const actualInstance = new WebAssembly.Instance(module, {
    "netwasm.host.v1": { service() { return 0; } },
  });
  const actual = fixture({
    instanceOverride: actualInstance,
    planOverrides: { canonicalInitializeExport: "run" },
  });
  actual.request.instantiate = async () => actualInstance;
  const actualSession = await actual.opener.open(actual.request);
  await actualSession.close();

  for (const mode of ["instance-null", "instance-exports", "boundary-null", "boundary-exports",
    "initialize-accessor", "initialize-missing", "wake-accessor", "wake-missing"]) {
    const state = fixture({ reactor: mode.startsWith("wake") });
    if (mode === "instance-null") state.request.instantiate = async () => null;
    if (mode === "instance-exports") state.request.instantiate = async () => ({ exports: null });
    if (mode === "boundary-null") {
      state.request.prepareInterop = () => ({
        consumeTerminalEvent() {},
        async drainTerminalReports() {},
        imports: {},
        close() {},
        bindInstance: () => null,
      });
    }
    if (mode === "boundary-exports") {
      state.request.prepareInterop = () => ({
        consumeTerminalEvent() {},
        async drainTerminalReports() {},
        imports: {},
        close() {},
        bindInstance: () => ({ exports: [] }),
      });
    }
    if (mode === "initialize-accessor") {
      Object.defineProperty(state.instance.exports, initializeExport, {
        enumerable: true, get: () => () => {},
      });
    }
    if (mode === "initialize-missing") delete state.instance.exports[initializeExport];
    if (mode === "wake-accessor") {
      Object.defineProperty(state.instance.exports, reactorGuest, {
        enumerable: true, get: () => () => {},
      });
    }
    if (mode === "wake-missing") delete state.instance.exports[reactorGuest];
    await assert.rejects(() => state.opener.open(state.request), TypeError);
  }
});

test("rejects malformed dependencies and opening requests before acquisition", async () => {
  const state = fixture();
  const validDependencies = {
    bindCanonicalInstance() {}, buildPlan() {}, closeCanonicalBinding() {}, composeImports() {},
    createCanonicalBinding() {}, createGuestNotifier() {}, createReactor() {},
    createReleaseCommand() {},
  };
  for (const dependencies of [null, [], {}, { ...validDependencies, extra() {} }, {
    ...validDependencies, [Symbol("invalid")]: true,
  }]) {
    assert.throws(() => createRawExportSessionOpener(dependencies), /dependencies/);
  }
  for (const name of Object.keys(validDependencies)) {
    assert.throws(() => createRawExportSessionOpener({
      ...validDependencies, [name]: null,
    }), /action/);
  }

  for (const request of [null, [], {}, { ...state.request, extra: true }, {
    ...state.request, [Symbol("invalid")]: true,
  }, Object.create(state.request)]) {
    await assert.rejects(() => state.opener.open(request), /request/);
  }
  for (const change of [
    { abi: null }, { abi: [] }, { adapter: null }, { adapter: [] }, { module: null },
    { providers: null }, { providers: [] },
  ]) {
    await assert.rejects(() => state.opener.open({ ...state.request, ...change }), /inputs/);
  }
  for (const change of [
    { instantiate: null }, { prepareInterop: null }, { schedule: null },
  ]) {
    await assert.rejects(() => state.opener.open({ ...state.request, ...change }), /actions/);
  }
  for (const releaseActions of [null, [null]]) {
    await assert.rejects(() => state.opener.open({ ...state.request, releaseActions }), /release/);
  }
});

test("rejects malformed interop, reactor and adapter metadata products", async () => {
  for (const interop of [null, [], {}, { imports: {}, bindInstance() {}, close() {}, extra: true },
    { imports: {}, bindInstance: null, close() {} },
    { imports: {}, bindInstance() {}, close: null },
    { imports: null, bindInstance() {}, close() {} },
    { imports: [], bindInstance() {}, close() {} },
    { imports: Object.create({}), bindInstance() {}, close() {} },
  ]) {
    const state = fixture();
    state.request.prepareInterop = () => interop;
    await assert.rejects(() => state.opener.open(state.request), /interop/);
  }

  for (const reactorProduct of [null, [], {}, { watch() {}, cancel() {}, close() {}, extra: true },
    { watch: null, cancel() {}, close() {} },
    { watch() {}, cancel: null, close() {} },
    { watch() {}, cancel() {}, close: null },
  ]) {
    const state = fixture({ reactor: true, reactorProduct });
    await assert.rejects(() => state.opener.open(state.request), /reactor/);
  }

  for (const rawAdapter of [
    {},
    { rawAdapterMetadata: null },
    { rawAdapterMetadata: 1 },
    { rawAdapterMetadata: {} },
    { rawAdapterMetadata: { requiredCapabilities: null } },
  ]) {
    const state = fixture();
    state.request.adapter = rawAdapter;
    const session = await state.opener.open(state.request);
    assert.equal(state.calls.includes("reactor"), false);
    await session.close();
  }
  const accessor = fixture();
  accessor.request.adapter = Object.defineProperty({}, "rawAdapterMetadata", {
    enumerable: true, get: () => ({}),
  });
  await assert.rejects(() => accessor.opener.open(accessor.request), /data property/);
});
