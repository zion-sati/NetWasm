import assert from "node:assert/strict";
import test from "node:test";
import { NetWasmHostError } from "./managed-errors.mjs";
import { createComponentExportSessionOpener } from "./component-export-session-opener.mjs";
import { createScopeReleaseCommand } from "./scope-release-command.mjs";

function fixture({
  failAt = null,
  guestWake = true,
  readyBeforeBind = false,
  releaseFailures = [],
  releaseCommandFactory = createScopeReleaseCommand,
} = {}) {
  const calls = [];
  const controls = {};
  const fail = name => {
    if (failAt === name) throw new Error(`${name} failed`);
  };
  const dependencies = {
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
      if (readyBeforeBind) request.onReady(3);
      return {
        cancel() { calls.push("reactor-cancel"); },
        close() {
          calls.push("close-reactor");
          if (releaseFailures.includes("reactor")) throw new Error("reactor close failed");
        },
        watch() { calls.push("reactor-watch"); },
      };
    },
    createReleaseCommand: releaseCommandFactory,
  };
  let counter = 0;
  const adapter = {
    contractKey: "netwasm:worker/wit@1.0.0",
    async instantiate(request) {
      calls.push("instantiate");
      controls.instantiateRequest = request;
      request.reactorHost.assertAvailable();
      fail("instantiate");
      return {
        exports: {
          add(value) {
            counter += value;
            calls.push(`add:${value}`);
            return counter;
          },
          read() {
            calls.push("read");
            return counter;
          },
        },
        guestWake: guestWake
          ? token => {
            calls.push(`wake:${token}`);
            fail("wake");
          }
          : null,
      };
    },
  };
  const request = {
    adapter,
    imports: Object.freeze({ provider: Object.freeze({}) }),
    instantiateCore() {},
    loadCoreModule() {},
    releaseActions: [() => {
      calls.push("close-caller");
      if (releaseFailures.includes("caller")) throw new Error("caller close failed");
    }],
    schedule: callback => callback(),
  };
  return {
    calls,
    controls,
    opener: createComponentExportSessionOpener(dependencies),
    request,
  };
}

test("instantiates once, retains state and closes exactly once", async () => {
  const state = fixture();
  const session = await state.opener.open(state.request);

  assert.equal(Object.isFrozen(state.opener), true);
  assert.equal(Object.isFrozen(session), true);
  assert.equal(Object.getPrototypeOf(session.exports), null);
  assert.equal(session.exports.add(20), 20);
  assert.equal(session.exports.add(22), 42);
  assert.equal(session.exports.read(), 42);
  assert.deepEqual(state.calls.slice(0, 5), [
    "reactor", "instantiate", "notifier", "add:20", "add:22",
  ]);
  assert.equal(Object.isFrozen(state.controls.instantiateRequest), true);
  assert.equal(state.controls.instantiateRequest.imports, state.request.imports);
  assert.equal(state.controls.instantiateRequest.instantiateCore,
    state.request.instantiateCore);
  assert.equal(state.controls.instantiateRequest.loadCoreModule,
    state.request.loadCoreModule);

  const firstClose = session.close();
  assert.equal(session.close(), firstClose);
  await firstClose;
  assert.equal(await session.failure, null);
  assert.deepEqual(state.calls.slice(-2), ["close-reactor", "close-caller"]);
  assert.throws(() => session.exports.read(), /closed/);
});

test("binds reactor delivery after component instantiation", async () => {
  const state = fixture();
  const session = await state.opener.open(state.request);

  state.controls.reactorRequest.onReady(7);
  assert.deepEqual(state.calls.slice(-2), ["notify:7", "wake:7"]);
  state.controls.instantiateRequest.reactorHost.watch();
  state.controls.instantiateRequest.reactorHost.cancel();
  assert.deepEqual(state.calls.slice(-2), ["reactor-watch", "reactor-cancel"]);

  await session.close();
  assert.throws(() => state.controls.instantiateRequest.reactorHost.assertAvailable(),
    /unavailable/);
});

test("supports components without a reactor guest export", async () => {
  const state = fixture({ guestWake: false });
  const session = await state.opener.open(state.request);

  assert.equal(session.exports.add(42), 42);
  assert.equal(state.calls.includes("notifier"), false);
  state.controls.reactorRequest.onReady(7);
  const failure = await session.failure;
  assert.equal(failure instanceof NetWasmHostError, true);
  assert.match(failure.message, /no guest wake binding/);
  await session.close();
});

test("rolls back when reactor readiness races guest binding", async () => {
  const state = fixture({ readyBeforeBind: true });

  await assert.rejects(() => state.opener.open(state.request), /before binding/);
  assert.deepEqual(state.calls.filter(value => value.startsWith("close-")), [
    "close-reactor", "close-caller",
  ]);
});

test("makes reactor and guest-wake failures terminal", async () => {
  for (const source of ["reactor", "wake"]) {
    const state = fixture({ failAt: source === "wake" ? "wake" : null });
    const session = await state.opener.open(state.request);
    if (source === "reactor") state.controls.reactorRequest.onFailure();
    else state.controls.reactorRequest.onReady(9);

    const failure = await session.failure;
    assert.equal(failure instanceof NetWasmHostError, true);
    assert.throws(() => session.exports.read(), error => error === failure);
    assert.throws(
      () => state.controls.instantiateRequest.reactorHost.assertAvailable(),
      error => error === failure);
    state.controls.reactorRequest.onFailure();
    state.controls.reactorRequest.onReady(10);
    await new Promise(resolve => setTimeout(resolve, 0));
    assert.deepEqual(state.calls.filter(value => value.startsWith("close-")), [
      "close-reactor", "close-caller",
    ]);
    await session.close();
  }
});

test("preserves a terminal failure when asynchronous cleanup also fails", async () => {
  const state = fixture({ releaseFailures: ["reactor"] });
  const session = await state.opener.open(state.request);

  state.controls.reactorRequest.onFailure();
  const failure = await session.failure;
  await new Promise(resolve => setTimeout(resolve, 0));

  assert.equal(failure instanceof NetWasmHostError, true);
  await assert.rejects(() => session.close(), AggregateError);
  assert.equal(await session.failure, failure);
});

test("rolls back startup failures and reports cleanup failures", async () => {
  const reactor = fixture({ failAt: "reactor" });
  await assert.rejects(() => reactor.opener.open(reactor.request), /reactor failed/);
  assert.deepEqual(reactor.calls.filter(value => value.startsWith("close-")), [
    "close-caller",
  ]);

  const startup = fixture({ failAt: "instantiate" });
  await assert.rejects(() => startup.opener.open(startup.request), /instantiate failed/);
  assert.deepEqual(startup.calls.filter(value => value.startsWith("close-")), [
    "close-reactor", "close-caller",
  ]);

  const cleanup = fixture({
    failAt: "instantiate",
    releaseFailures: ["reactor", "caller"],
  });
  await assert.rejects(() => cleanup.opener.open(cleanup.request), error =>
    error instanceof AggregateError && error.errors.length === 3);

  const closing = fixture({ releaseFailures: ["reactor", "caller"] });
  const session = await closing.opener.open(closing.request);
  await assert.rejects(() => session.close(), error =>
    error instanceof AggregateError && error.errors.length === 2);
  assert.equal((await session.failure) instanceof AggregateError, true);
});

test("validates dependencies, requests, boundaries and exported operations", async () => {
  const state = fixture();
  assert.throws(() => createComponentExportSessionOpener({}), /shape/);
  assert.throws(() => createComponentExportSessionOpener({
    createGuestNotifier() {},
    createReactor: 1,
    createReleaseCommand() {},
  }), /createReactor action/);
  await assert.rejects(() => state.opener.open(), /request shape/);
  await assert.rejects(() => state.opener.open(null), /request is invalid/);
  await assert.rejects(() => state.opener.open({ ...state.request, extra: true }), /shape/);

  for (const invalid of [
    { ...state.request, adapter: { contractKey: "", instantiate() {} } },
    { ...state.request, adapter: { contractKey: "contract", instantiate: 1 } },
    { ...state.request, imports: [] },
    { ...state.request, loadCoreModule: 1 },
    { ...state.request, instantiateCore: 1 },
    { ...state.request, releaseActions: [1] },
  ]) {
    await assert.rejects(() => state.opener.open(invalid), TypeError);
  }

  const accessorImports = Object.defineProperty({}, "provider", {
    enumerable: true,
    get() { return {}; },
  });
  await assert.rejects(() => state.opener.open({
    ...state.request,
    imports: accessorImports,
  }), /plain data object/);

  const invalidReactor = fixture();
  invalidReactor.request.adapter = state.request.adapter;
  invalidReactor.opener = createComponentExportSessionOpener({
    createGuestNotifier() {},
    createReactor: () => ({ cancel() {}, close() {}, watch: 1 }),
    createReleaseCommand: createScopeReleaseCommand,
  });
  await assert.rejects(() => invalidReactor.opener.open(invalidReactor.request),
    /reactor actions/);

  for (const boundary of [null, {}, { exports: {}, guestWake() {} },
    { exports: { bad: 1 }, guestWake() {} },
    { exports: { read() {} }, guestWake: 1 }]) {
    const current = fixture();
    current.request.adapter = {
      ...current.request.adapter,
      instantiate: async () => boundary,
    };
    await assert.rejects(() => current.opener.open(current.request), TypeError);
  }
});

test("rejects calls while close is in progress", async () => {
  let release;
  const state = fixture();
  state.request.releaseActions = [() => new Promise(resolve => { release = resolve; })];
  const session = await state.opener.open(state.request);

  const closing = session.close();
  assert.throws(() => session.exports.read(), /closed/);
  assert.throws(() => state.controls.instantiateRequest.reactorHost.assertAvailable(),
    /unavailable/);
  await Promise.resolve();
  release();
  await closing;
});

test("reports release-command rejection and requires a scheduler", async () => {
  const rejected = new Error("release rejected");
  const state = fixture({
    releaseCommandFactory: () => ({ close: () => Promise.reject(rejected) }),
  });
  const session = await state.opener.open(state.request);
  await assert.rejects(() => session.close(), error => error === rejected);
  assert.equal(await session.failure, rejected);

  const platformScheduler = fixture();
  delete platformScheduler.request.schedule;
  const platformSession = await platformScheduler.opener.open(platformScheduler.request);
  await platformSession.close();

  const missing = fixture();
  delete missing.request.schedule;
  const original = globalThis.queueMicrotask;
  try {
    globalThis.queueMicrotask = undefined;
    await assert.rejects(() => missing.opener.open(missing.request), /actions/);
  } finally {
    globalThis.queueMicrotask = original;
  }
});
