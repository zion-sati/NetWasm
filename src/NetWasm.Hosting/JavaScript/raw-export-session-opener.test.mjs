import assert from "node:assert/strict";
import test from "node:test";
import { NetWasmHostError } from "./managed-errors.mjs";
import { createRawExportSessionOpener } from "./raw-export-session-opener.mjs";
import { createScopeReleaseCommand } from "./scope-release-command.mjs";

const initializeExport = "cm32p2_initialize";
const runExport = "netwasm.events.run";
const callbackExport = "netwasm.events.callback";
const root = Object.freeze({ "[waitable-set-new]"() { return 1; } });

function fixture({
  failAt = null,
  instanceOverride = null,
  onInteropClose = null,
  onInitialize = null,
  pendingExport = false,
  planOverrides = {},
  probeExportAtPrepare = false,
  pumpProduct,
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
        canonicalInitializeExport: initializeExport,
        eventLoopRunExport: runExport,
        eventLoopCallbackExport: callbackExport,
        ...planOverrides,
      };
    },
    closeCanonicalBinding() {
      calls.push("close-canonical");
      if (releaseFailures.includes("canonical")) throw new Error("canonical cleanup failed");
    },
    composeImports(request) {
      calls.push("compose");
      controls.composeRequest = request;
      fail("compose");
      return Object.freeze(Object.create(null));
    },
    createCanonicalBinding(request) {
      calls.push("canonical");
      controls.canonicalRequest = request;
      fail("canonical");
      controls.canonicalBinding = Object.freeze({ imports: Object.freeze({}), target: "wasm32" });
      return controls.canonicalBinding;
    },
    createEventPump(request) {
      calls.push("pump");
      controls.pumpRequest = request;
      fail("pump");
      if (pumpProduct !== undefined) return pumpProduct;
      return {
        turn() { calls.push("turn"); },
        stop() { calls.push("stop"); },
      };
    },
    createReleaseCommand: releaseCommandFactory,
    readComponentModel(request) {
      calls.push("component-model");
      assert.equal(request.binding, controls.canonicalBinding);
      controls.componentModel = Object.freeze({ root });
      return controls.componentModel;
    },
  };
  const instance = instanceOverride ?? {
    exports: {
      marker: 37,
      [initializeExport]() {
        calls.push(`initialize:${this.marker}`);
        onInitialize?.(controls);
        fail("initialize");
      },
      [runExport]() { return 0; },
      [callbackExport]() { return 0; },
    },
  };
  const request = {
    abi: {},
    adapter: {},
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
              version: 3,
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
  };
  return {
    calls,
    controls,
    instance,
    opener: createRawExportSessionOpener(dependencies),
    request,
  };
}

test("opens, initializes and closes an export session exactly once", async () => {
  const state = fixture();
  const session = await state.opener.open(state.request);

  assert.equal(Object.isFrozen(state.opener), true);
  assert.equal(Object.isFrozen(session), true);
  assert.equal(session.exports.read(), 42);
  assert.deepEqual(state.calls, [
    "plan", "prepare-interop", "canonical", "component-model", "compose", "instantiate",
    "bind-interop", "bind-canonical", "pump", "initialize:37", "turn", "read", "turn",
  ]);
  assert.equal(session.exports.version, 3);
  assert.equal(state.controls.pumpRequest.componentModel, state.controls.componentModel);
  assert.equal(Object.isFrozen(state.controls.pumpRequest), true);
  state.controls.pumpRequest.run();
  state.controls.pumpRequest.callback(0, 0, 0);
  state.controls.lifetime.observeAsyncCompletion();
  assert.equal(state.calls.at(-1), "turn");
  assert.deepEqual(Object.keys(state.controls.canonicalRequest).sort(), ["adapter", "providers"]);
  assert.equal(state.controls.composeRequest.physicalProviders.$root, root);
  assert.deepEqual(state.controls.composeRequest.inventory, { target: "wasm32", imports: [] });
  assert.doesNotThrow(() => state.controls.lifetime.assertAsyncDeliveryAvailable());
  assert.equal(Object.isFrozen(state.controls.instantiateRequest), true);
  const firstClose = session.close();
  const secondClose = session.close();
  assert.equal(firstClose, secondClose);
  await firstClose;
  assert.equal(await session.failure, null);
  assert.deepEqual(state.calls.slice(-4), [
    "stop", "close-interop", "close-canonical", "close-caller",
  ]);
  assert.throws(() => session.exports.read(), /closed/);
  assert.equal(state.calls.at(-1), "turn");
  assert.throws(() => state.controls.lifetime.assertAsyncDeliveryAvailable(), /closed/);
});

test("runs the module's event loop after each call, completion and failure", async () => {
  const state = fixture();
  const session = await state.opener.open(state.request);
  assert.equal(state.calls.filter(value => value === "turn").length, 1);
  session.exports.read();
  state.controls.lifetime.observeAsyncCompletion(undefined);
  assert.equal(state.calls.filter(value => value === "turn").length, 3);
  state.controls.pumpRequest.onFailure(new Error("event loop trapped"));
  const failure = await session.failure;
  assert.match(failure.message, /export session failed/);
  assert.ok(state.calls.includes("stop"));
  assert.throws(() => session.exports.read(), error => error === failure);
  assert.equal(state.calls.at(-1), "turn");
  await new Promise(resolve => setTimeout(resolve, 0));
  await session.close();

  const completion = fixture();
  const completed = await completion.opener.open(completion.request);
  completion.controls.lifetime.observeAsyncCompletion(new Error("delivery failed"));
  assert.match((await completed.failure).message, /export session failed/);
  await new Promise(resolve => setTimeout(resolve, 0));
  await completed.close();
});

test("makes the first asynchronous failure permanent", async () => {
  const state = fixture({ pendingExport: true });
  const session = await state.opener.open(state.request);
  const pending = session.exports.pending();
  state.controls.lifetime.observeAsyncFailure(new Error("private failure"));
  const failure = await session.failure;
  assert.equal(failure instanceof NetWasmHostError, true);
  assert.throws(() => session.exports.read(), error => error === failure);
  assert.throws(() => state.controls.lifetime.assertAsyncDeliveryAvailable(),
    error => error === failure);
  state.controls.lifetime.observeAsyncFailure(new Error("later"));
  assert.equal(await session.failure, failure);
  await assert.rejects(pending, /closed/);
  await new Promise(resolve => setTimeout(resolve, 0));
  assert.deepEqual(state.calls.filter(value => value.startsWith("close-")),
    ["close-interop", "close-canonical", "close-caller"]);
  await session.close();
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

test("rejects export access before readiness", async () => {
  const state = fixture({ probeExportAtPrepare: true });
  const session = await state.opener.open(state.request);
  assert.match(state.controls.beforeReadyExportError.message, /not ready/);
  await session.close();
});

test("keeps the Component Model's own built-ins from the interop imports", async () => {
  const state = fixture();
  const prepare = state.request.prepareInterop;
  state.request.prepareInterop = lifetime => ({ ...prepare(lifetime), imports: { $root: {} } });
  await assert.rejects(() => state.opener.open(state.request), /reserved/);
  assert.deepEqual(state.calls.filter(value => value.startsWith("close-")), ["close-interop", "close-caller"]);
});

test("rolls back every acquired scope for each startup failure", async () => {
  const cases = [
    ["plan", ["close-caller"]],
    ["prepare-interop", ["close-caller"]],
    ["canonical", ["close-interop", "close-caller"]],
    ["compose", ["close-interop", "close-canonical", "close-caller"]],
    ["instantiate", ["close-interop", "close-canonical", "close-caller"]],
    ["bind-interop", ["close-interop", "close-canonical", "close-caller"]],
    ["bind-canonical", ["close-interop", "close-canonical", "close-caller"]],
    ["pump", ["close-interop", "close-canonical", "close-caller"]],
    ["initialize", ["close-interop", "close-canonical", "close-caller"]],
  ];
  for (const [failAt, expectedClosing] of cases) {
    const state = fixture({ failAt });
    await assert.rejects(() => state.opener.open(state.request), new RegExp(failAt));
    assert.deepEqual(state.calls.filter(value => value.startsWith("close-")), expectedClosing);
  }
});

test("attempts all cleanup and reports close and rollback failures", async () => {
  const closing = fixture({
    releaseFailures: ["interop", "canonical", "caller"],
  });
  const session = await closing.opener.open(closing.request);
  await assert.rejects(() => session.close(), error =>
    error instanceof AggregateError && error.errors.length === 3);
  assert.equal((await session.failure) instanceof AggregateError, true);
  assert.deepEqual(closing.calls.slice(-3), [
    "close-interop", "close-canonical", "close-caller",
  ]);

  const rollback = fixture({
    failAt: "initialize",
    releaseFailures: ["interop", "canonical"],
  });
  await assert.rejects(() => rollback.opener.open(rollback.request), error =>
    error instanceof AggregateError && error.errors.length === 3);
  assert.deepEqual(rollback.calls.slice(-3), [
    "close-interop", "close-canonical", "close-caller",
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
  await firstClose;
  assert.equal(nestedClose, firstClose);
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
    planOverrides: { canonicalInitializeExport: "run", eventLoopRunExport: "run", eventLoopCallbackExport: "run" },
  });
  actual.request.instantiate = async () => actualInstance;
  const actualSession = await actual.opener.open(actual.request);
  await actualSession.close();

  for (const mode of ["instance-null", "instance-exports", "boundary-null", "boundary-exports",
    "initialize-accessor", "initialize-missing"]) {
    const state = fixture();
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
    await assert.rejects(() => state.opener.open(state.request), TypeError);
  }
});

test("rejects malformed dependencies and opening requests before acquisition", async () => {
  const state = fixture();
  const validDependencies = {
    bindCanonicalInstance() {}, buildPlan() {}, closeCanonicalBinding() {}, composeImports() {},
    createCanonicalBinding() {}, createEventPump() {}, createReleaseCommand() {}, readComponentModel() {},
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
    { instantiate: null }, { prepareInterop: null },
  ]) {
    await assert.rejects(() => state.opener.open({ ...state.request, ...change }), /actions/);
  }
  for (const releaseActions of [null, [null]]) {
    await assert.rejects(() => state.opener.open({ ...state.request, releaseActions }), /release/);
  }
});

test("rejects a malformed event pump", async () => {
  for (const pumpProduct of [null, 1, {}, { turn() {} }, { stop() {} }]) {
    const state = fixture({ pumpProduct });
    await assert.rejects(() => state.opener.open(state.request), /event pump/);
  }
  const unavailable = fixture({ planOverrides: { eventLoopRunExport: "missing" } });
  await assert.rejects(() => unavailable.opener.open(unavailable.request), /'missing' is unavailable/);
});

test("rejects malformed interop products", async () => {
  for (const interop of [null, [], {}, { imports: {}, bindInstance() {}, close() {}, extra: true },
    { imports: {}, bindInstance: null, close() {} },
    { imports: {}, bindInstance() {}, close: null },
    { imports: null, bindInstance() {}, close() {} },
    { imports: [], bindInstance() {}, close() {} },
    { imports: Object.create({}), bindInstance() {}, close() {} },
    { imports: {}, bindInstance: null, close() {}, consumeTerminalEvent() {}, async drainTerminalReports() {} },
    Object.assign(Object.create({ inherited: true }), {
      imports: {}, bindInstance() {}, close() {}, consumeTerminalEvent() {}, async drainTerminalReports() {},
    }),
  ]) {
    const state = fixture();
    state.request.prepareInterop = () => interop;
    await assert.rejects(() => state.opener.open(state.request), /interop/);
  }

});
