import assert from "node:assert/strict";
import test from "node:test";
import { createLinkedCorpusObserver } from "./linked-corpus-observer.mjs";
import { readCorpusTrace } from "./corpus-trace-reader.mjs";

const request = { input: 1, exposesLegacyTrace: false, usesTypedTrace: false };
const emptyTrace = () => ({ trace: 0, traceRecords: [] });

function fixture(run, readTrace = emptyTrace) {
  let disposed = 0;
  let created = 0;
  const observer = createLinkedCorpusObserver({
    instantiate: async report => {
      created++;
      return {
        instance: { exports: { run: input => run(input, report) } },
        dispose() { disposed++; },
      };
    },
    readTrace,
  });
  return { observer, disposed: () => disposed, created: () => created };
}

test("fresh instances preserve values and are disposed once per input", async () => {
  const state = fixture(input => input);
  for (const input of [-2147483648, 0, 2147483647]) {
    assert.deepEqual(await state.observer.observe({ ...request, input }), {
      kind: "value", value: input, exceptionTypeId: null, trace: 0, traceRecords: [],
    });
  }
  assert.equal(state.created(), 3);
  assert.equal(state.disposed(), 3);
});

test("one terminal event plus a terminal trap preserves managed type identity", async () => {
  const state = fixture((input, report) => {
    if (input === 1) {
      report({ typeId: 17 });
      throw new WebAssembly.RuntimeError("terminal");
    }
    return 42;
  });
  assert.deepEqual(await state.observer.observe(request), {
    kind: "exception", value: null, exceptionTypeId: 17, trace: 0, traceRecords: [],
  });
  assert.equal((await state.observer.observe({ ...request, input: 2 })).kind, "value");
  assert.equal(state.disposed(), 2);
});

test("unreported Wasm traps and escaping Wasm exceptions remain traps", async () => {
  const tag = new WebAssembly.Tag({ parameters: [] });
  for (const cause of [new WebAssembly.RuntimeError("trap"), new WebAssembly.Exception(tag, [])]) {
    const state = fixture(() => { throw cause; });
    assert.deepEqual(await state.observer.observe(request), {
      kind: "trap", value: null, exceptionTypeId: null, trace: 0, traceRecords: [],
    });
    assert.equal(state.disposed(), 1);
  }
});

test("host errors are infrastructure failures, not managed exceptions", async () => {
  const cause = new Error("host failed");
  const state = fixture((input, report) => {
    report({ typeId: 17 });
    throw cause;
  });
  await assert.rejects(state.observer.observe(request), error => error === cause);
  assert.equal(state.disposed(), 1);
});

test("inconsistent terminal events never become successful observations", async () => {
  for (const action of [
    report => { report({ typeId: 1 }); return 42; },
    report => { report({ typeId: 1 }); report({ typeId: 2 }); throw new WebAssembly.RuntimeError("terminal"); },
    report => { report({ typeId: 1 }); throw new WebAssembly.Exception(new WebAssembly.Tag({ parameters: [] }), []); },
  ]) {
    const state = fixture((input, report) => action(report));
    await assert.rejects(state.observer.observe(request), TypeError);
    assert.equal(state.disposed(), 1);
  }
});

test("invalid terminal identities are rejected and instances disposed", async () => {
  for (const event of [null, {}, { typeId: 0 }, { typeId: -1 }, { typeId: 1.5 }, { typeId: 2147483648 }]) {
    const state = fixture((input, report) => {
      report(event);
      throw new WebAssembly.RuntimeError("terminal");
    });
    await assert.rejects(state.observer.observe(request), TypeError);
    assert.equal(state.disposed(), 1);
  }
});

test("invalid values are rejected without coercion", async () => {
  for (const value of [undefined, null, "42", 42n, NaN, Infinity, 1.5, -2147483649, 2147483648]) {
    const state = fixture(() => value);
    await assert.rejects(state.observer.observe(request), TypeError);
    assert.equal(state.disposed(), 1);
  }
});

test("invalid requests fail before any instance exists", async () => {
  const state = fixture(() => 42);
  for (const invalid of [null, {}, { ...request, input: 1.5 }, { ...request, input: -2147483649 },
    { ...request, input: 2147483648 }, { ...request, exposesLegacyTrace: null }, { ...request, usesTypedTrace: null }])
    await assert.rejects(state.observer.observe(invalid), TypeError);
  assert.equal(state.created(), 0);
  assert.equal(state.disposed(), 0);
});

test("capabilities are required and instantiation failure propagates unchanged", async () => {
  assert.throws(() => createLinkedCorpusObserver({ instantiate: null, readTrace: emptyTrace }), TypeError);
  assert.throws(() => createLinkedCorpusObserver({ instantiate() {}, readTrace: null }), TypeError);
  const cause = new Error("instantiation failed");
  const observer = createLinkedCorpusObserver({ instantiate: async () => { throw cause; }, readTrace: emptyTrace });
  await assert.rejects(observer.observe(request), error => error === cause);
});

test("malformed instances fail and available disposal still runs", async () => {
  let disposed = 0;
  for (const managed of [null, {}, { dispose() { disposed++; } },
    { instance: {}, dispose() { disposed++; } },
    { instance: { exports: {} }, dispose() { disposed++; } }]) {
    const observer = createLinkedCorpusObserver({ instantiate: async () => managed, readTrace: emptyTrace });
    await assert.rejects(observer.observe(request), TypeError);
  }
  assert.equal(disposed, 3);
});

test("initialization terminal events stop invocation and trigger disposal", async () => {
  let invoked = false;
  let disposed = false;
  const observer = createLinkedCorpusObserver({
    instantiate: async report => {
      report({ typeId: 1 });
      return { instance: { exports: { run() { invoked = true; } } }, dispose() { disposed = true; } };
    },
    readTrace: emptyTrace,
  });
  await assert.rejects(observer.observe(request), TypeError);
  assert.equal(invoked, false);
  assert.equal(disposed, true);
});

test("trace requests preserve flags and exports before disposal", async () => {
  const calls = [];
  const exports = { run: () => 42, trace: () => -7, trace_count: () => 0 };
  const observer = createLinkedCorpusObserver({
    instantiate: async () => ({ instance: { exports }, dispose() { calls.push("dispose"); } }),
    readTrace(actual) {
      calls.push("trace");
      assert.equal(actual.exports, exports);
      assert.equal(actual.exposesLegacyTrace, true);
      assert.equal(actual.usesTypedTrace, true);
      return readCorpusTrace(actual);
    },
  });
  assert.deepEqual(await observer.observe({ ...request, exposesLegacyTrace: true, usesTypedTrace: true }), {
    kind: "value", value: 42, exceptionTypeId: null, trace: -7, traceRecords: [],
  });
  assert.deepEqual(calls, ["trace", "dispose"]);
});

test("trace and disposal failures cannot produce a successful observation", async () => {
  const cause = new Error("trace failed");
  const state = fixture(() => 42, () => { throw cause; });
  await assert.rejects(state.observer.observe(request), error => error === cause);
  assert.equal(state.disposed(), 1);
  const observer = createLinkedCorpusObserver({
    instantiate: async () => ({ instance: { exports: { run: () => 42 } }, dispose() { throw cause; } }),
    readTrace: emptyTrace,
  });
  await assert.rejects(observer.observe(request), error => error === cause);
});

test("managed exceptions and traps retain traces read after invocation and before disposal", async () => {
  for (const managedException of [false, true]) {
    let invoked = false;
    let reads = 0;
    const trace = { trace: -7, traceRecords: [{ kind: 3, eventId: 2, payloadLow: -1, payloadHigh: 0 }] };
    const state = fixture((input, report) => {
      invoked = true;
      if (managedException) report({ typeId: 17 });
      throw new WebAssembly.RuntimeError("terminal");
    }, actual => {
      assert.equal(invoked, true);
      assert.equal(state.disposed(), 0);
      assert.equal(actual.exposesLegacyTrace, true);
      assert.equal(actual.usesTypedTrace, true);
      reads++;
      return trace;
    });
    assert.deepEqual(await state.observer.observe({ ...request, exposesLegacyTrace: true, usesTypedTrace: true }), {
      kind: managedException ? "exception" : "trap", value: null,
      exceptionTypeId: managedException ? 17 : null, ...trace,
    });
    assert.equal(reads, 1);
    assert.equal(state.disposed(), 1);
  }
});

test("a disposal failure retains the original invocation failure as well", async () => {
  const primary = new Error("host failure");
  const cleanup = new Error("cleanup failure");
  const observer = createLinkedCorpusObserver({
    instantiate: async () => ({
      instance: { exports: { run() { throw primary; } } },
      dispose() { throw cleanup; },
    }),
    readTrace: emptyTrace,
  });
  await assert.rejects(observer.observe(request), error => {
    assert.ok(error instanceof AggregateError);
    assert.deepEqual(error.errors, [primary, cleanup]);
    return true;
  });
});
