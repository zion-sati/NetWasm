import assert from "node:assert/strict";
import test from "node:test";
import { createWorkerSessionClient } from "./worker-session-client.mjs";
import { createWorkerSessionHost } from "./worker-session-host.mjs";

const digest = "1".repeat(64);
const startup = Object.freeze({ buildFingerprint: digest, manifestSha256: digest });

class LinkedWorker {
  listeners = new Map();
  host = null;
  terminations = 0;

  addEventListener(kind, listener) {
    const values = this.listeners.get(kind) ?? [];
    values.push(listener);
    this.listeners.set(kind, values);
  }
  removeEventListener(kind, listener) {
    this.listeners.set(kind, (this.listeners.get(kind) ?? []).filter(value => value !== listener));
  }
  postMessage(message) {
    queueMicrotask(() => this.host.receive(message).catch(cause => {
      this.emit("error", { message: cause instanceof Error ? cause.message : "worker failed" });
    }));
  }
  terminate() { this.terminations++; }
  emit(kind, value) {
    for (const listener of [...(this.listeners.get(kind) ?? [])]) listener(value);
  }
}

function deferred() {
  let resolve;
  let reject;
  const promise = new Promise((yes, no) => { resolve = yes; reject = no; });
  return { promise, resolve, reject };
}

function fixture(overrides = {}) {
  const messages = [];
  const opened = [];
  const failure = deferred();
  let closes = 0;
  let terminations = 0;
  const options = {
    async openSession(request) {
      opened.push(request);
      return {
        exports: { add: (left, right) => left + right },
        close: async () => { closes++; },
        failure: failure.promise,
      };
    },
    post: message => messages.push(message),
    terminate: () => { terminations++; },
    ...overrides,
  };
  return {
    closes: () => closes,
    failure,
    host: createWorkerSessionHost(options),
    messages,
    opened,
    options,
    terminations: () => terminations,
  };
}

const initialize = (overrides = {}) => ({
  protocolVersion: 1, generation: "generation-1", kind: "initialize", startup, ...overrides,
});
const invoke = (overrides = {}) => ({
  protocolVersion: 1, generation: "generation-1", kind: "invoke",
  requestId: 1, operation: "add", arguments: [20, 22], ...overrides,
});

test("opens one session before readiness and delegates allowlisted calls", async () => {
  const state = fixture();
  await state.host.receive(initialize());
  assert.equal(Object.isFrozen(state.opened[0]), true);
  assert.equal(state.opened[0].generation, "generation-1");
  assert.deepEqual(state.opened[0].startup, startup);
  assert.equal(typeof state.opened[0].notify, "function");
  assert.deepEqual(state.messages[0], {
    protocolVersion: 1, generation: "generation-1", kind: "ready", operations: ["add"],
  });
  await state.host.receive(invoke());
  assert.equal(state.messages[1].kind, "result");
  assert.equal(state.messages[1].value, 42);
});

test("reports startup failures with readable messages and terminates", async () => {
  const empty = new Error("");
  empty.stack = undefined;
  for (const cause of [new Error("startup failed"), "private", empty]) {
    const state = fixture({ openSession: async () => { throw cause; } });
    await state.host.receive(initialize());
    assert.equal(state.messages[0].kind, "failure");
    assert.equal(state.messages[0].requestId, null);
    assert.equal(state.messages[0].error.category, "startupFailure");
    assert.equal(state.messages[0].error.message,
      cause instanceof Error && cause.message.length !== 0 ? "startup failed" : "worker failed");
    if (cause === empty) assert.equal(state.messages[0].error.stack, null);
    assert.equal(state.terminations(), 1);
    await state.host.receive(invoke());
  }
});

test("reports terminal session failure once and stops the worker", async () => {
  const state = fixture();
  await state.host.receive(initialize());
  state.failure.resolve(new Error("session failed"));
  await new Promise(resolve => setImmediate(resolve));
  assert.equal(state.messages[1].kind, "failure");
  assert.equal(state.messages[1].error.category, "hostFailure");
  assert.equal(state.messages[1].error.operation, "session");
  assert.equal(state.terminations(), 1);
  state.host.terminate();
  assert.equal(state.terminations(), 1);

  const rejected = fixture();
  await rejected.host.receive(initialize());
  rejected.failure.reject(new Error("failure promise rejected"));
  await new Promise(resolve => setImmediate(resolve));
  assert.equal(rejected.messages[1].error.message, "failure promise rejected");
});

test("closes a session that opens after hard termination", async () => {
  const opening = deferred();
  let closes = 0;
  const state = fixture({ openSession: () => opening.promise });
  const startup = state.host.receive(initialize());
  state.host.terminate();
  opening.resolve({
    exports: {}, close: async () => { closes++; }, failure: new Promise(() => {}),
  });
  await startup;
  assert.equal(closes, 1);
  assert.equal(state.messages.length, 0);
});

test("treats messages during startup and duplicate initialization as terminal", async () => {
  const opening = deferred();
  const state = fixture({ openSession: () => opening.promise });
  const startup = state.host.receive(initialize());
  await state.host.receive(invoke());
  assert.equal(state.messages[0].error.message, "worker is not ready");
  assert.equal(state.terminations(), 1);
  opening.resolve({ exports: {}, close: async () => {}, failure: new Promise(() => {}) });
  await startup;

  const duplicate = fixture();
  await duplicate.host.receive(initialize());
  await duplicate.host.receive(initialize());
  assert.equal(duplicate.messages.at(-1).kind, "failure");
  assert.equal(duplicate.terminations(), 1);
});

test("contains post and terminate failures", async () => {
  let undeliveredCause = null;
  const postFailure = fixture({
    post() { throw new Error("post failed"); },
    terminate(cause) { undeliveredCause = cause; },
  });
  await postFailure.host.receive(initialize());
  assert.equal(undeliveredCause.message, "post failed");

  const terminateFailure = fixture({ terminate() { throw new Error("private"); } });
  terminateFailure.host.terminate();
  terminateFailure.host.terminate();

  let posts = 0;
  const deliveryFailure = fixture({ post(message) {
    posts++;
    if (message.kind !== "ready") throw new Error("delivery failed");
    deliveryFailure.messages.push(message);
  } });
  await deliveryFailure.host.receive(initialize());
  await deliveryFailure.host.receive(invoke());
  assert.equal(posts, 4);
  assert.equal(deliveryFailure.terminations(), 1);
});

test("settles the client queue when a result cannot be cloned", async () => {
  const worker = new LinkedWorker();
  let failFirstResult = true;
  const host = createWorkerSessionHost({
    openSession: async () => ({
      exports: { add: (left, right) => left + right },
      close: async () => {},
      failure: new Promise(() => {}),
    }),
    post(message) {
      if (message.kind === "result" && failFirstResult) {
        failFirstResult = false;
        const failure = new Error("value could not be cloned");
        failure.name = "DataCloneError";
        throw failure;
      }
      queueMicrotask(() => worker.emit("message", { data: message }));
    },
    terminate: () => worker.emit("error", { message: "worker terminated" }),
  });
  worker.host = host;
  const client = createWorkerSessionClient({
    generation: "generation-1", operations: ["add"], startup, worker,
  });

  await client.ready;
  const active = client.invoke("add", [20, 22]);
  const queued = client.invoke("add", [1, 2]);
  const disposal = client.dispose();
  await assert.rejects(active, error => error.category === "hostFailure"
    && /could not be cloned/.test(error.message));
  assert.equal(await queued, 3);
  await disposal;
  assert.equal(worker.terminations, 1);
});

test("rejects malformed composition, initialization and session products", async () => {
  const valid = fixture().options;
  for (const value of [null, [], {}, { ...valid, extra: true }, {
    ...valid, [Symbol("invalid")]: true,
  }]) assert.throws(() => createWorkerSessionHost(value), TypeError);
  for (const key of ["openSession", "post", "terminate"]) {
    assert.throws(() => createWorkerSessionHost({ ...valid, [key]: null }), /actions/);
  }
  for (const value of [
    null, [], {}, { ...initialize(), extra: true }, initialize({ protocolVersion: 2 }),
    initialize({ kind: "other" }), initialize({ generation: "" }),
    initialize({ generation: "bad\0generation" }), initialize({ generation: "x".repeat(129) }),
    initialize({ startup: null }), initialize({ startup: {} }),
    initialize({ startup: { ...startup, extra: true } }),
    initialize({ startup: { ...startup, buildFingerprint: "invalid" } }),
    initialize({ startup: { ...startup, manifestSha256: "invalid" } }),
    Object.defineProperty(initialize(), "kind", { enumerable: true, get: () => "initialize" }),
  ]) {
    const state = fixture();
    await assert.rejects(() => state.host.receive(value), TypeError);
  }
  for (const product of [null, [], {}, { exports: null, close() {}, failure: Promise.resolve() },
    { exports: {}, close: null, failure: Promise.resolve() },
    { exports: {}, close() {}, failure: null }]) {
    const state = fixture({ openSession: async () => product });
    await state.host.receive(initialize());
    assert.equal(state.messages[0].kind, "failure");
  }
});


test("application-defined notifications post during work and stop after close", async () => {
  let notify;
  const state = fixture({
    async openSession(request) {
      notify = request.notify;
      return {
        exports: { calculate() { notify("custom-progress", [42, 100]); notify("custom-detail", ["working"]); return 42; } },
        close: async () => {}, failure: new Promise(() => {}),
      };
    },
  });
  await state.host.receive(initialize());
  await state.host.receive(invoke({ operation: "calculate", arguments: [] }));
  assert.deepEqual(state.messages.map(message => message.kind), ["ready", "notification", "notification", "result"]);
  assert.equal(state.messages[1].generation, "generation-1");
  assert.deepEqual(state.messages[1].arguments, [42, 100]);
  assert.equal(state.messages[1].operation, "custom-progress");
  assert.throws(() => notify("", []), TypeError);
  assert.throws(() => notify("valid", null), TypeError);
  await state.host.receive({ protocolVersion: 1, generation: "generation-1", kind: "close" });
  await new Promise(resolve => setImmediate(resolve));
  const count = state.messages.length;
  notify("late", []);
  assert.equal(state.messages.length, count);
});
