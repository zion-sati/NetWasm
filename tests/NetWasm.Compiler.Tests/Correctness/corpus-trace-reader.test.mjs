import assert from "node:assert/strict";
import test from "node:test";
import { readCorpusTrace } from "./corpus-trace-reader.mjs";

test("undeclared traces do not invoke exports or invent records", () => {
  assert.deepEqual(readCorpusTrace({
    exports: { trace() { throw new Error("must not run"); } },
    exposesLegacyTrace: false, usesTypedTrace: false,
  }), { trace: 0, traceRecords: [] });
});

test("legacy and typed traces preserve signed words and event order", () => {
  const calls = [];
  const exports = {
    trace: () => -2147483648,
    trace_count: () => 2,
    trace_kind(index) { calls.push(index); return index; },
    trace_event_id: index => 7 + index,
    trace_payload_low: index => index === 0 ? -2147483648 : 2147483647,
    trace_payload_high: index => index === 0 ? -1 : 0,
  };
  assert.deepEqual(readCorpusTrace({ exports, exposesLegacyTrace: true, usesTypedTrace: true }), {
    trace: -2147483648,
    traceRecords: [
      { kind: 0, eventId: 7, payloadLow: -2147483648, payloadHigh: -1 },
      { kind: 1, eventId: 8, payloadLow: 2147483647, payloadHigh: 0 },
    ],
  });
  assert.deepEqual(calls, [0, 1]);
});

test("either trace contract can be selected independently", () => {
  assert.deepEqual(readCorpusTrace({
    exports: { trace: () => 42 }, exposesLegacyTrace: true, usesTypedTrace: false,
  }), { trace: 42, traceRecords: [] });
  assert.deepEqual(readCorpusTrace({
    exports: { trace_count: () => 0 }, exposesLegacyTrace: false, usesTypedTrace: true,
  }), { trace: 0, traceRecords: [] });
});

test("trace declaration flags must be explicit booleans", () => {
  for (const value of [undefined, null, 0, "false"]) {
    assert.throws(() => readCorpusTrace({ exposesLegacyTrace: value, usesTypedTrace: false }), TypeError);
    assert.throws(() => readCorpusTrace({ exposesLegacyTrace: false, usesTypedTrace: value }), TypeError);
  }
});

test("every declared trace export must exist and return an i32", () => {
  assert.throws(() => readCorpusTrace({ exposesLegacyTrace: true, usesTypedTrace: false }), TypeError);
  assert.throws(() => readCorpusTrace({ exports: {}, exposesLegacyTrace: false, usesTypedTrace: true }), TypeError);
  for (const value of [NaN, Infinity, 1.5, -2147483649, 2147483648, 1n, "1"]) {
    assert.throws(() => readCorpusTrace({
      exports: { trace: () => value }, exposesLegacyTrace: true, usesTypedTrace: false,
    }), TypeError);
  }
  for (const missing of ["trace_kind", "trace_event_id", "trace_payload_low", "trace_payload_high"]) {
    const exports = {
      trace_count: () => 1, trace_kind: () => 0, trace_event_id: () => 1,
      trace_payload_low: () => 2, trace_payload_high: () => 3,
    };
    delete exports[missing];
    assert.throws(() => readCorpusTrace({ exports, exposesLegacyTrace: false, usesTypedTrace: true }), TypeError);
  }
});

test("negative counts and export failures propagate", () => {
  assert.throws(() => readCorpusTrace({
    exports: { trace_count: () => -1 }, exposesLegacyTrace: false, usesTypedTrace: true,
  }), RangeError);
  const cause = new Error("trace failed");
  assert.throws(() => readCorpusTrace({
    exports: { trace() { throw cause; } }, exposesLegacyTrace: true, usesTypedTrace: false,
  }), error => error === cause);
});
