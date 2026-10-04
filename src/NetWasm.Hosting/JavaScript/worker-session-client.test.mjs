import assert from "node:assert/strict";
import test from "node:test";
import {
  createWorkerSessionClient,
  NetWasmWorkerError,
} from "./worker-session-client.mjs";

const digest = "1".repeat(64);
const startup = Object.freeze({ buildFingerprint: digest, manifestSha256: digest });

class FakeWorker {
  listeners = new Map();
  sent = [];
  terminations = 0;
  postFailure = null;
  terminateFailure = null;

  addEventListener(kind, listener) {
    const values = this.listeners.get(kind) ?? [];
    values.push(listener);
    this.listeners.set(kind, values);
  }
  removeEventListener(kind, listener) {
    this.listeners.set(kind, (this.listeners.get(kind) ?? []).filter(value => value !== listener));
  }
  postMessage(message) {
    if (this.postFailure !== null) throw this.postFailure;
    this.sent.push(message);
  }
  terminate() {
    this.terminations++;
    if (this.terminateFailure !== null) throw this.terminateFailure;
  }
  emit(kind, value = {}) {
    for (const listener of [...(this.listeners.get(kind) ?? [])]) listener(value);
  }
}

function fixture(overrides = {}) {
  const worker = overrides.worker ?? new FakeWorker();
  const client = createWorkerSessionClient({
    generation: "generation-1",
    operations: ["add", "fail"],
    startup,
    worker,
    ...overrides,
  });
  return { client, worker };
}

const ready = (overrides = {}) => ({ data: {
  protocolVersion: 1,
  generation: "generation-1",
  kind: "ready",
  operations: ["add", "fail"],
  ...overrides,
} });
const result = (requestId, value, overrides = {}) => ({ data: {
  protocolVersion: 1, generation: "generation-1", kind: "result", requestId, value,
  ...overrides,
} });
const failure = (requestId, overrides = {}) => ({ data: {
  protocolVersion: 1, generation: "generation-1", kind: "failure", requestId,
  error: { category: "guestFailure", message: "managed failure", operation: "fail", stack: "remote" },
  ...overrides,
} });
const closed = (overrides = {}) => ({ data: {
  protocolVersion: 1, generation: "generation-1", kind: "closed", ...overrides,
} });

test("starts one generation and dispatches accepted invocations through one FIFO", async () => {
  const { client, worker } = fixture();
  assert.equal(client.generation, "generation-1");
  assert.deepEqual(worker.sent, [{
    protocolVersion: 1, generation: "generation-1", kind: "initialize", startup,
  }]);
  const first = client.invoke("add", [20, 22]);
  const second = client.invoke("add", [1, 2]);
  assert.equal(worker.sent.length, 1);
  worker.emit("message", ready());
  await client.ready;
  assert.equal(worker.sent[1].requestId, 1);
  assert.equal(worker.sent.length, 2);
  worker.emit("message", result(1, 42));
  assert.equal(await first, 42);
  assert.equal(worker.sent[2].requestId, 2);
  worker.emit("message", result(2, 3));
  assert.equal(await second, 3);
});

test("snapshots byte views when calls are accepted", async () => {
  const { client, worker } = fixture();
  const backing = new Uint8Array(8);
  const input = backing.subarray(2, 5);
  input.set([1, 2, 3]);
  const pending = client.invoke("add", [input]);
  backing.fill(9);
  structuredClone(backing.buffer, { transfer: [backing.buffer] });
  worker.emit("message", ready());
  await client.ready;
  assert.deepEqual(worker.sent.at(-1).arguments[0], Uint8Array.of(1, 2, 3));
  assert.equal(worker.sent.at(-1).arguments[0].buffer.byteLength, 3);
  assert.notEqual(worker.sent.at(-1).arguments[0], input);
  worker.emit("message", result(1, 6));
  assert.equal(await pending, 6);

  if (typeof SharedArrayBuffer === "function") {
    const shared = new Uint8Array(new SharedArrayBuffer(3));
    shared.set([4, 5, 6]);
    const sharedPending = client.invoke("add", [shared]);
    shared.fill(8);
    assert.deepEqual(worker.sent.at(-1).arguments[0], Uint8Array.of(4, 5, 6));
    assert.equal(worker.sent.at(-1).arguments[0].buffer instanceof SharedArrayBuffer, false);
    worker.emit("message", result(2, 15));
    assert.equal(await sharedPending, 15);
  }

  const memory = new WebAssembly.Memory({ initial: 1 });
  const wasmView = new Uint8Array(memory.buffer, 10, 3);
  wasmView.set([7, 8, 9]);
  const wasmPending = client.invoke("add", [wasmView]);
  memory.grow(1);
  assert.deepEqual(worker.sent.at(-1).arguments[0], Uint8Array.of(7, 8, 9));
  worker.emit("message", result(3, 24));
  assert.equal(await wasmPending, 24);
});

test("projects remote failures with a readable stable envelope", async () => {
  const { client, worker } = fixture();
  worker.emit("message", ready());
  await client.ready;
  const pending = client.invoke("fail");
  worker.emit("message", failure(1));
  await assert.rejects(pending, error => error instanceof NetWasmWorkerError
    && error.message === "managed failure" && error.category === "guestFailure"
    && error.operation === "fail" && error.remoteStack === "remote");
});

test("preserves managed worker details separately from the remote JavaScript stack", async () => {
  for (const message of ["original 🌏", ""]) {
    const { client, worker } = fixture();
    worker.emit("message", ready());
    await client.ready;
    const managed = { typeId: 7, typeName: "System.FormatException", message, stackTrace: "managed trace" };
    const pending = client.invoke("fail");
    worker.emit("message", failure(1, { error: {
      category: "guestFailure", message, operation: "fail", stack: "JavaScript stack", managed,
    } }));
    await assert.rejects(pending, error => error instanceof NetWasmWorkerError
      && error.message === message && error.remoteStack === "JavaScript stack"
      && error.managed.message === message && error.managed.stackTrace === "managed trace"
      && error.managed.typeId === 7 && Object.isFrozen(error.managed));
    const subsequent = client.invoke("add", [20, 22]);
    worker.emit("message", result(2, 42));
    assert.equal(await subsequent, 42);
    client.terminate();
  }
});

test("rejects malformed managed failure envelopes before exposing them to the page", async () => {
  const managed = { typeId: 7, typeName: null, message: "original", stackTrace: null };
  const envelope = { category: "guestFailure", message: "original", operation: "fail", stack: null, managed };
  for (const error of [
    { ...envelope, managed: {} }, { ...envelope, managed: null },
    { ...envelope, category: "hostFailure" }, { ...envelope, message: "" },
    { ...envelope, [Symbol()]: true },
    Object.defineProperty({ ...envelope }, "managed", { enumerable: true, get() { assert.fail("getter"); } }),
    Object.defineProperty({ ...envelope }, "managed", { enumerable: false, value: managed }),
  ]) {
    const { client, worker } = fixture();
    worker.emit("message", ready());
    await client.ready;
    const pending = client.invoke("fail");
    worker.emit("message", failure(1, { error }));
    await assert.rejects(pending, TypeError);
    assert.equal(worker.terminations, 1);
  }
});

test("cancels queued waits without dispatching and retains active queue order", async () => {
  const { client, worker } = fixture();
  worker.emit("message", ready());
  await client.ready;
  const activeController = new AbortController();
  const active = client.invoke("add", [1], { signal: activeController.signal });
  const queuedController = new AbortController();
  const queued = client.invoke("add", [2], { signal: queuedController.signal });
  const next = client.invoke("add", [3]);

  queuedController.abort();
  await assert.rejects(queued, error => error instanceof NetWasmWorkerError
    && error.category === "cancelled" && error.operation === "add");
  assert.equal(worker.sent.filter(message => message.kind === "invoke").length, 1);

  activeController.abort();
  await assert.rejects(active, error => error instanceof NetWasmWorkerError
    && error.category === "cancelled" && error.operation === "add");
  assert.equal(worker.sent.filter(message => message.kind === "invoke").length, 1);

  worker.emit("message", result(1, 1));
  assert.equal(worker.sent.at(-1).requestId, 3);
  worker.emit("message", result(3, 3));
  assert.equal(await next, 3);

  const aborted = new AbortController();
  aborted.abort();
  const sent = worker.sent.length;
  await assert.rejects(
    client.invoke("add", [4], { signal: aborted.signal }),
    error => error instanceof NetWasmWorkerError && error.category === "cancelled");
  assert.equal(worker.sent.length, sent);
});

test("times out caller waits while containing late guest completion", async () => {
  const { client, worker } = fixture();
  worker.emit("message", ready());
  await client.ready;
  const timed = client.invoke("add", [1], { timeoutMilliseconds: 0 });
  const next = client.invoke("add", [2]);
  await assert.rejects(timed, error => error instanceof NetWasmWorkerError
    && error.category === "deadlineExceeded" && error.operation === "add");
  assert.equal(worker.sent.filter(message => message.kind === "invoke").length, 1);
  worker.emit("message", result(1, 1));
  assert.equal(worker.sent.at(-1).requestId, 2);
  worker.emit("message", result(2, 2));
  assert.equal(await next, 2);
});

test("a stale deadline callback cannot cancel a later request", async t => {
  let deadline;
  t.mock.method(globalThis, "setTimeout", callback => { deadline = callback; return 123; });
  const cleared = [];
  t.mock.method(globalThis, "clearTimeout", timer => cleared.push(timer));
  const { client, worker } = fixture();
  worker.emit("message", ready());
  await client.ready;
  const first = client.invoke("add", [1], { timeoutMilliseconds: 100 });
  worker.emit("message", result(1, 42));
  assert.equal(await first, 42);
  assert.deepEqual(cleared, [123]);
  const second = client.invoke("add", [2]);
  deadline();
  worker.emit("message", result(2, 43));
  assert.equal(await second, 43);
  assert.equal(worker.terminations, 0);
  client.terminate();
});

test("graceful disposal drains accepted work then closes and terminates", async () => {
  const { client, worker } = fixture();
  worker.emit("message", ready());
  await client.ready;
  const first = client.invoke("add", [1, 1]);
  const second = client.invoke("add", [2, 2]);
  const disposal = client.dispose();
  assert.equal(client.dispose(), disposal);
  await assert.rejects(() => client.invoke("add"), /closed/);
  worker.emit("message", result(1, 2));
  assert.equal(await first, 2);
  assert.equal(worker.sent.at(-1).requestId, 2);
  worker.emit("message", result(2, 4));
  assert.equal(await second, 4);
  assert.equal(worker.sent.at(-1).kind, "close");
  worker.emit("message", closed());
  await disposal;
  assert.equal(worker.terminations, 1);
  assert.equal(await client.dispose(), undefined);
  client.terminate();
  assert.equal(worker.terminations, 1);
});

test("disposal requested during startup waits for readiness then closes", async () => {
  const { client, worker } = fixture();
  const disposal = client.dispose();
  assert.equal(worker.sent.length, 1);
  worker.emit("message", ready());
  await client.ready;
  assert.equal(worker.sent.at(-1).kind, "close");
  worker.emit("message", closed());
  await disposal;
});

test("hard termination invalidates startup and every accepted request", async () => {
  const { client, worker } = fixture();
  const retainedMessageListener = worker.listeners.get("message")[0];
  const pending = client.invoke("add");
  client.terminate();
  client.terminate();
  await assert.rejects(client.ready, /terminated/);
  await assert.rejects(pending, /terminated/);
  await assert.rejects(() => client.invoke("add"), /closed/);
  assert.equal(await client.dispose(), undefined);
  retainedMessageListener(ready());
  assert.equal(worker.terminations, 1);

  const readyState = fixture();
  readyState.worker.emit("message", ready());
  await readyState.client.ready;
  const active = readyState.client.invoke("add");
  const queued = readyState.client.invoke("add");
  const disposal = readyState.client.dispose();
  readyState.client.terminate();
  await assert.rejects(active, /terminated/);
  await assert.rejects(queued, /terminated/);
  await assert.rejects(disposal, /terminated/);
});

test("worker failures reject startup, active, queued and disposal state", async () => {
  for (const [kind, event, pattern] of [
    ["error", { message: "browser worker failed" }, /browser worker failed/],
    ["error", {}, /worker execution failed/],
    ["messageerror", {}, /message delivery failed/],
  ]) {
    const starting = fixture();
    const retainedFailureListener = starting.worker.listeners.get(kind)[0];
    starting.worker.emit(kind, event);
    await assert.rejects(starting.client.ready, pattern);
    const disposal = starting.client.dispose();
    assert.equal(starting.client.dispose(), disposal);
    await assert.rejects(disposal, pattern);
    assert.equal(starting.worker.terminations, 1);
    retainedFailureListener(event);
  }

  const running = fixture();
  running.worker.emit("message", ready());
  await running.client.ready;
  const active = running.client.invoke("add");
  const queued = running.client.invoke("add");
  const disposal = running.client.dispose();
  running.worker.emit("error", { message: "failed" });
  await assert.rejects(active, /failed/);
  await assert.rejects(queued, /failed/);
  await assert.rejects(disposal, /failed/);
  running.worker.emit("error", { message: "later" });

  const terminal = fixture();
  terminal.worker.emit("message", ready());
  await terminal.client.ready;
  terminal.worker.emit("message", failure(null));
  assert.equal(terminal.worker.terminations, 1);
});

test("ignores stale generations and faults malformed or unexpected responses", async () => {
  const stale = fixture();
  stale.worker.emit("message", ready({ generation: "old" }));
  let settled = false;
  stale.client.ready.finally(() => { settled = true; });
  await Promise.resolve();
  assert.equal(settled, false);
  stale.worker.emit("message", ready());
  await stale.client.ready;

  const cases = [
    { data: null }, { data: [] }, { data: {} }, ready({ protocolVersion: 2 }),
    ready({ operations: ["add"] }), ready({ operations: null }), ready({ extra: true }),
    ready({ operations: ["add", "add"] }),
    { data: { protocolVersion: 1, generation: "generation-1", kind: "other" } },
    result(7, 1), result(0, 1), result(1, 1, { requestId: null }), closed(),
    failure(null, { error: null }),
    failure(null, { error: { category: "", message: "", operation: 1, stack: 1 } }),
    { data: Object.defineProperty(result(1, 1).data, "value", {
      enumerable: true, get: () => 1,
    }) },
  ];
  for (const event of cases) {
    const state = fixture();
    if (event !== cases[0] && event.data?.kind !== "ready") {
      state.worker.emit("message", ready());
      await state.client.ready;
    }
    state.worker.emit("message", event);
    if (state.client.ready) await assert.rejects(state.client.ready, TypeError).catch(() => {});
    assert.equal(state.worker.terminations, 1);
  }
});

test("contains post and terminate failures and validates composition", async () => {
  const postWorker = new FakeWorker();
  postWorker.postFailure = new Error("post failed");
  const postClient = createWorkerSessionClient({
    generation: "generation-1", operations: [], startup, worker: postWorker,
  });
  await assert.rejects(postClient.ready, /post failed/);

  const nonErrorWorker = new FakeWorker();
  nonErrorWorker.postFailure = "private";
  const nonErrorClient = createWorkerSessionClient({
    generation: "generation-1", operations: [], startup, worker: nonErrorWorker,
  });
  await assert.rejects(nonErrorClient.ready, /worker session failed/);

  const invokePost = fixture();
  invokePost.worker.emit("message", ready());
  await invokePost.client.ready;
  invokePost.worker.postFailure = new Error("invoke post failed");
  await assert.rejects(invokePost.client.invoke("add"), /invoke post failed/);
  assert.equal(invokePost.worker.terminations, 1);

  const closePost = fixture();
  closePost.worker.emit("message", ready());
  await closePost.client.ready;
  closePost.worker.postFailure = new Error("close post failed");
  await assert.rejects(closePost.client.dispose(), /close post failed/);

  const failedTerminate = fixture();
  failedTerminate.worker.emit("message", ready());
  await failedTerminate.client.ready;
  failedTerminate.worker.terminateFailure = new Error("private failure");
  failedTerminate.worker.emit("error", { message: "public failure" });
  assert.equal(failedTerminate.worker.terminations, 1);

  const terminateWorker = new FakeWorker();
  terminateWorker.terminateFailure = new Error("private terminate failure");
  const terminateClient = createWorkerSessionClient({
    generation: "generation-1", operations: [], startup, worker: terminateWorker,
  });
  terminateClient.terminate();
  await assert.rejects(terminateClient.ready, /terminated/);

  const valid = {
    generation: "generation-1", operations: [], startup, worker: new FakeWorker(),
  };
  for (const value of [null, [], {}, { ...valid, extra: true }, {
    ...valid, [Symbol("invalid")]: true,
  }]) assert.throws(() => createWorkerSessionClient(value), TypeError);
  for (const generation of [null, "", "x".repeat(129), "bad\0generation"]) {
    assert.throws(() => createWorkerSessionClient({ ...valid, generation }), /generation/);
  }
  for (const operations of [null, [null], [""], ["same", "same"]]) {
    assert.throws(() => createWorkerSessionClient({ ...valid, operations }), /allowlist/);
  }
  for (const value of [null, {}, { ...startup, extra: true },
    { ...startup, buildFingerprint: "invalid" }, { ...startup, manifestSha256: "invalid" }]) {
    assert.throws(() => createWorkerSessionClient({ ...valid, startup: value }),
      /startup|fingerprint|digest/);
  }
  for (const worker of [null, {}, { ...new FakeWorker(), postMessage() {} }]) {
    assert.throws(() => createWorkerSessionClient({ ...valid, worker }), /Worker/);
  }
  const state = fixture();
  await assert.rejects(() => state.client.invoke("missing"), /unavailable/);
  await assert.rejects(() => state.client.invoke("add", null), /array/);
  for (const options of [
    null, [], { extra: true }, { signal: {} }, { timeoutMilliseconds: -1 },
    { timeoutMilliseconds: 1.5 }, { timeoutMilliseconds: 2_147_483_648 },
    Object.defineProperty({}, "signal", { enumerable: true, get: () => null }),
  ]) await assert.rejects(() => state.client.invoke("add", [], options), /options|signal|timeout/);
});


test("native notifications preserve names and values without releasing the active call", async () => {
  const seen = [];
  const handlerWork = new Promise(() => {});
  const { client, worker } = fixture({ onNotification: value => { seen.push(value); return handlerWork; } });
  worker.emit("message", ready());
  await client.ready;
  const first = client.invoke("add", [20, 22]);
  const second = client.invoke("add", [1, 2]);
  const arguments_ = [42, "diagnostic", { total: 100 }, Uint8Array.of(1, 2), 9007199254740993n];
  worker.emit("message", { data: {
    protocolVersion: 1, generation: "generation-1", kind: "notification",
    operation: "my-app/custom-event", arguments: arguments_,
  } });
  assert.deepEqual(seen, [{ operation: "my-app/custom-event", arguments: arguments_ }]);
  assert.equal(worker.sent.length, 2);
  worker.emit("message", result(1, 42));
  assert.equal(await first, 42);
  assert.equal(worker.sent.length, 3);
  worker.emit("message", result(2, 3));
  assert.equal(await second, 3);
  client.terminate();
  worker.emit("message", { data: {
    protocolVersion: 1, generation: "generation-1", kind: "notification",
    operation: "late", arguments: [],
  } });
  assert.equal(seen.length, 1);
});

test("notification handlers are optional and their errors remain caller-local", async t => {
  const errors = [];
  const original = globalThis.reportError;
  globalThis.reportError = cause => errors.push(cause);
  t.after(() => { if (original === undefined) delete globalThis.reportError; else globalThis.reportError = original; });
  for (const handler of [undefined, () => { throw new Error("handler failure"); }, async () => { throw new Error("async handler failure"); }]) {
    const { client, worker } = fixture(handler === undefined ? {} : { onNotification: handler });
    worker.emit("message", ready());
    await client.ready;
    const call = client.invoke("add", [20, 22]);
    worker.emit("message", { data: {
      protocolVersion: 1, generation: "generation-1", kind: "notification",
      operation: "notice", arguments: ["hello"],
    } });
    worker.emit("message", result(1, 42));
    assert.equal(await call, 42);
    await Promise.resolve();
    client.terminate();
  }
  assert.deepEqual(errors.map(error => error.message), ["handler failure", "async handler failure"]);
  assert.throws(() => fixture({ onNotification: 42 }), TypeError);
});

test("notification errors use console reporting when reportError is unavailable", async t => {
  const original = globalThis.reportError;
  delete globalThis.reportError;
  t.after(() => { if (original !== undefined) globalThis.reportError = original; });
  const reported = [];
  t.mock.method(console, "error", cause => reported.push(cause));
  const cause = new Error("notification failure");
  const { client, worker } = fixture({ onNotification() { throw cause; } });
  worker.emit("message", ready());
  await client.ready;
  const call = client.invoke("add", [20, 22]);
  worker.emit("message", { data: {
    protocolVersion: 1, generation: "generation-1", kind: "notification",
    operation: "notice", arguments: [],
  } });
  worker.emit("message", result(1, 42));
  assert.equal(await call, 42);
  assert.deepEqual(reported, [cause]);
  client.terminate();
});

test("malformed live notifications fail clearly while stale notifications are ignored", async () => {
  const { client, worker } = fixture();
  worker.emit("message", ready());
  await client.ready;
  const call = client.invoke("add");
  const failed = assert.rejects(call, TypeError);
  worker.emit("message", { data: {
    protocolVersion: 1, generation: "old", kind: "notification", operation: "", arguments: null,
  } });
  assert.equal(worker.terminations, 0);
  worker.emit("message", { data: {
    protocolVersion: 1, generation: "generation-1", kind: "notification", operation: "", arguments: [],
  } });
  await failed;
  assert.equal(worker.terminations, 1);
});
