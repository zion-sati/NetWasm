import assert from "node:assert/strict";
import test from "node:test";
import { NetWasmManagedError } from "./managed-errors.mjs";
import { createWorkerSessionDispatcher } from "./worker-session-dispatcher.mjs";

function fixture(overrides = {}) {
  const messages = [];
  const failures = [];
  let closes = 0;
  const options = {
    close: async () => { closes++; },
    exports: { add: (left, right) => left + right },
    generation: "generation-1",
    onFailure: cause => failures.push(cause),
    post: message => messages.push(message),
    ...overrides,
  };
  return {
    dispatcher: createWorkerSessionDispatcher(options),
    failures,
    messages,
    options,
    closes: () => closes,
  };
}

function invoke(overrides = {}) {
  return {
    protocolVersion: 1,
    generation: "generation-1",
    kind: "invoke",
    requestId: 1,
    operation: "add",
    arguments: [20, 22],
    ...overrides,
  };
}

const close = () => ({ protocolVersion: 1, generation: "generation-1", kind: "close" });

test("dispatches only allowlisted operations and returns values", async () => {
  const state = fixture();
  assert.deepEqual(state.dispatcher.operations, ["add"]);
  assert.equal(Object.isFrozen(state.dispatcher.operations), true);
  await state.dispatcher.receive(invoke());
  assert.deepEqual(state.messages, [{
    protocolVersion: 1, generation: "generation-1", kind: "result",
    requestId: 1, value: 42,
  }]);

  await state.dispatcher.receive(invoke({ requestId: 2, operation: "missing" }));
  assert.equal(state.messages[1].kind, "failure");
  assert.equal(state.messages[1].error.category, "contractFailure");
});

test("does not queue concurrent invocations inside the worker", async () => {
  let resolve;
  const calls = [];
  const state = fixture({ exports: {
    wait: value => { calls.push(value); return new Promise(done => { resolve = done; }); },
  } });
  const first = state.dispatcher.receive(invoke({ operation: "wait", arguments: [1] }));
  await state.dispatcher.receive(invoke({ requestId: 2, operation: "wait", arguments: [2] }));
  assert.deepEqual(calls, [1]);
  assert.equal(state.messages[0].error.category, "contractFailure");
  resolve(7);
  await first;
  assert.equal(state.messages[1].value, 7);
});

test("classifies managed failures, traps and host failures", async () => {
  const managed = new NetWasmManagedError("managed");
  const trap = new WebAssembly.RuntimeError("trap");
  const empty = new Error("");
  empty.stack = undefined;
  for (const [cause, expected] of [
    [managed, "guestFailure"], [trap, "trap"], [new Error("host"), "hostFailure"],
    ["private", "hostFailure"], [empty, "hostFailure"],
  ]) {
    const state = fixture({ exports: { fail() { throw cause; } } });
    await state.dispatcher.receive(invoke({ operation: "fail", arguments: [] }));
    assert.equal(state.messages[0].error.category, expected);
    assert.equal(state.messages[0].error.operation, "fail");
    if (cause === empty) {
      assert.equal(state.messages[0].error.message, "worker invocation failed");
      assert.equal(state.messages[0].error.stack, null);
    }
  }
});

test("sends clone-safe managed payloads and accepts later invocations", async () => {
  for (const message of ["original 🌏", "", null]) {
    const managed = { typeId: 7, typeName: "System.FormatException", message, stackTrace: "managed trace" };
    const error = new NetWasmManagedError("fail", { managed });
    const state = fixture({ exports: { fail() { throw error; }, add: (a, b) => a + b } });
    await state.dispatcher.receive(invoke({ operation: "fail", arguments: [] }));
    const envelope = structuredClone(state.messages[0].error);
    assert.equal(envelope.category, "guestFailure");
    assert.equal(envelope.message, message ?? managed.typeName);
    assert.deepEqual(envelope.managed, managed);
    assert.equal(envelope.stack, error.stack);
    await state.dispatcher.receive(invoke({ requestId: 2 }));
    assert.equal(state.messages[1].value, 42);
    assert.deepEqual(state.failures, []);
  }
});

test("defers graceful close until the active invocation settles", async () => {
  let resolve;
  const state = fixture({ exports: {
    wait: () => new Promise(done => { resolve = done; }),
  } });
  const pending = state.dispatcher.receive(invoke({ operation: "wait", arguments: [] }));
  await state.dispatcher.receive(close());
  assert.equal(state.closes(), 0);
  await state.dispatcher.receive(invoke({ requestId: 2 }));
  assert.equal(state.messages[0].error.category, "hostFailure");
  resolve(42);
  await pending;
  await new Promise(resolveTick => setImmediate(resolveTick));
  assert.equal(state.closes(), 1);
  assert.equal(state.messages.at(-1).kind, "closed");
  await state.dispatcher.receive(close());
  assert.equal(state.closes(), 1);
});

test("reports close and post failures through the terminal observer", async () => {
  const closeFailure = new Error("close failed");
  const closing = fixture({ close: () => Promise.reject(closeFailure) });
  await closing.dispatcher.receive(close());
  await new Promise(resolve => setImmediate(resolve));
  assert.equal(closing.messages[0].kind, "failure");
  assert.equal(closing.messages[0].requestId, null);
  assert.deepEqual(closing.failures, [closeFailure]);

  const postFailure = new Error("post failed");
  const posting = fixture({ post() { throw postFailure; } });
  await posting.dispatcher.receive(invoke());
  assert.deepEqual(posting.failures, [postFailure]);

  const cloneFailure = new Error("value could not be cloned");
  cloneFailure.name = "DataCloneError";
  const cloneSafe = fixture({ post(message) {
    if (message.kind === "result") throw cloneFailure;
    cloneSafe.messages.push(message);
  } });
  await cloneSafe.dispatcher.receive(invoke());
  assert.equal(cloneSafe.messages[0].kind, "failure");
  assert.equal(cloneSafe.messages[0].requestId, 1);
  assert.equal(cloneSafe.messages[0].error.category, "hostFailure");
  assert.match(cloneSafe.messages[0].error.message, /could not be cloned/);
  assert.deepEqual(cloneSafe.failures, []);

  const observer = fixture({ post() { throw postFailure; }, onFailure() { throw new Error("observer"); } });
  await observer.dispatcher.receive(invoke());
});

test("rejects malformed composition and request shapes before invocation", async () => {
  const valid = fixture().options;
  for (const value of [null, [], {}, { ...valid, extra: true }, {
    ...valid, [Symbol("invalid")]: true,
  }]) assert.throws(() => createWorkerSessionDispatcher(value), TypeError);
  for (const key of ["close", "onFailure", "post"]) {
    assert.throws(() => createWorkerSessionDispatcher({ ...valid, [key]: null }), /actions/);
  }
  for (const generation of [null, "", "x".repeat(129), "bad\0generation"]) {
    assert.throws(() => createWorkerSessionDispatcher({ ...valid, generation }), /generation/);
  }
  for (const exports of [null, [], { add: 1 }, { __proto__: () => {} },
    Object.defineProperty({}, "add", { enumerable: true, get: () => () => {} })]) {
    assert.throws(() => createWorkerSessionDispatcher({ ...valid, exports }), /export/);
  }

  const state = fixture();
  const cases = [
    null, [], {}, { ...invoke(), extra: true }, { ...invoke(), protocolVersion: 2 },
    { ...invoke(), generation: "stale" }, { ...invoke(), requestId: 0 },
    { ...invoke(), requestId: 1.5 }, { ...invoke(), operation: "" },
    { ...invoke(), arguments: null }, { ...invoke(), kind: "other" },
    { ...close(), extra: true }, { ...close(), kind: "other" },
    Object.defineProperty(invoke(), "operation", { enumerable: true, get: () => "add" }),
  ];
  for (const value of cases) await assert.rejects(() => state.dispatcher.receive(value), TypeError);
});
