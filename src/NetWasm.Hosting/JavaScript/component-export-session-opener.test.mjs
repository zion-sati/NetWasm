import assert from "node:assert/strict";
import test from "node:test";
import { createComponentExportSessionOpener } from "./component-export-session-opener.mjs";
import { createScopeReleaseCommand } from "./scope-release-command.mjs";

function fixture({
  failAt = null,
  releaseFailures = [],
  releaseCommandFactory = createScopeReleaseCommand,
} = {}) {
  const calls = [];
  const controls = {};
  let counter = 0;
  const adapter = {
    contractKey: "netwasm:worker/wit@1.0.0",
    async instantiate(request) {
      calls.push("instantiate");
      controls.instantiateRequest = request;
      if (failAt === "instantiate") throw new Error("instantiate failed");
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
  };
  return {
    calls,
    controls,
    opener: createComponentExportSessionOpener(Object.freeze({ createReleaseCommand: releaseCommandFactory })),
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
  assert.deepEqual(state.calls, ["instantiate", "add:20", "add:22", "read"]);
  assert.equal(Object.isFrozen(state.controls.instantiateRequest), true);
  assert.deepEqual(Object.keys(state.controls.instantiateRequest).sort(),
    ["imports", "instantiateCore", "loadCoreModule"]);
  assert.equal(state.controls.instantiateRequest.imports, state.request.imports);
  assert.equal(state.controls.instantiateRequest.instantiateCore, state.request.instantiateCore);
  assert.equal(state.controls.instantiateRequest.loadCoreModule, state.request.loadCoreModule);

  const firstClose = session.close();
  assert.equal(session.close(), firstClose);
  await firstClose;
  assert.equal(await session.failure, null);
  assert.deepEqual(state.calls.slice(-1), ["close-caller"]);
  assert.throws(() => session.exports.read(), /closed/);
});

test("rolls back startup failures and reports cleanup failures", async () => {
  const startup = fixture({ failAt: "instantiate" });
  await assert.rejects(() => startup.opener.open(startup.request), /instantiate failed/);
  assert.deepEqual(startup.calls.filter(value => value.startsWith("close-")), ["close-caller"]);

  const cleanup = fixture({ failAt: "instantiate", releaseFailures: ["caller"] });
  await assert.rejects(() => cleanup.opener.open(cleanup.request), error =>
    error instanceof AggregateError && error.errors.length === 2);

  const closing = fixture({ releaseFailures: ["caller"] });
  const session = await closing.opener.open(closing.request);
  await assert.rejects(() => session.close(), error =>
    error instanceof AggregateError && error.errors.length === 1);
  assert.equal((await session.failure) instanceof AggregateError, true);
});

test("validates dependencies, requests, boundaries and exported operations", async () => {
  const state = fixture();
  assert.throws(() => createComponentExportSessionOpener({}), /shape/);
  assert.throws(() => createComponentExportSessionOpener({ createReleaseCommand: 1 }), /createReleaseCommand action/);
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
  await assert.rejects(() => state.opener.open({ ...state.request, imports: accessorImports }),
    /plain data object/);

  for (const boundary of [null, {}, { exports: {} }, { exports: { bad: 1 } },
    { exports: { read() {} }, guestWake: null }]) {
    const current = fixture();
    current.request.adapter = { ...current.request.adapter, instantiate: async () => boundary };
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
  await Promise.resolve();
  release();
  await closing;
});

test("reports a release-command rejection", async () => {
  const rejected = new Error("release rejected");
  const state = fixture({
    releaseCommandFactory: () => ({ close: () => Promise.reject(rejected) }),
  });
  const session = await state.opener.open(state.request);
  await assert.rejects(() => session.close(), error => error === rejected);
  assert.equal(await session.failure, rejected);
});
