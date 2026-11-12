import assert from "node:assert/strict";
import test from "node:test";
import { createManagedExceptionReporter, copyManagedString } from "./managed-exception-reporter.mjs";
import { symbolSidecar, typeSymbol } from "./symbol-sidecar-reader.test.mjs";

const buildId = "b".repeat(32);
const typeSymbols = (entries, options) => symbolSidecar(options?.kind ?? 1, entries, options);


async function reportArtifactOutcome(artifacts) {
  let resolveEnriched;
  const enrichment = new Promise(resolve => { resolveEnriched = resolve; });
  let enriched;
  const reporter = createManagedExceptionReporter({
    getMemory: () => new WebAssembly.Memory({ initial: 1 }),
    stringDataOffset: 8,
    reportImmediate: () => {},
    reportEnriched: event => {
      enriched = event;
      resolveEnriched();
    },
    loadArtifacts: async () => artifacts,
  });
  reporter.reportTerminalException(7, 0, 0);
  await enrichment;
  return enriched;
}

test("copies UTF-16 synchronously, including unpaired surrogates", () => {
  const memory = new WebAssembly.Memory({ initial: 1 });
  new Uint16Array(memory.buffer, 24, 3).set([0x41, 0xd800, 0x42]);

  assert.equal(copyManagedString(memory, 16, 3, 8), "A\ud800B");
});

test("reports immediately and enriches lazily with one cached artifact load", async () => {
  const memory = new WebAssembly.Memory({ initial: 1 });
  new Uint16Array(memory.buffer, 40, 2).set([0x68, 0x69]);
  new Uint16Array(memory.buffer, 56, 2).set([0x61, 0x74]);
  const immediate = [];
  const enriched = [];
  let resolveEnriched;
  const enrichments = new Promise(resolve => { resolveEnriched = resolve; });
  let loads = 0;
  const reporter = createManagedExceptionReporter({
    getMemory: () => memory,
    stringDataOffset: 8,
    reportImmediate: event => immediate.push(event),
    reportEnriched: event => {
      enriched.push(event);
      if (enriched.length === 2) resolveEnriched();
    },
    loadArtifacts: async () => {
      loads++;
      return { typeSymbols: typeSymbols([typeSymbol(7, "BoomException")]) };
    },
  });

  reporter.reportTerminalException(7, 32, 2, 48, 2);
  reporter.reportTerminalException(7, 32, 2, 48, 2);
  assert.deepEqual(immediate.map(event => event.message), ["hi", "hi"]);
  assert.deepEqual(immediate.map(event => event.stackTrace), ["at", "at"]);
  assert.deepEqual(immediate.map(event => event.eventId), [1, 2]);
  await enrichments;
  assert.equal(loads, 1);
  assert.deepEqual(enriched.map(event => event.typeName), ["BoomException", "BoomException"]);
  assert.deepEqual(enriched.map(event => event.stackTrace), ["at", "at"]);
});

test("supports the v1 import while preserving null stack-trace semantics", async () => {
  const reports = [];
  const reporter = createManagedExceptionReporter({
    getMemory: () => new WebAssembly.Memory({ initial: 1 }),
    stringDataOffset: 8,
    reportImmediate: report => reports.push(report),
    reportEnriched() {},
    loadArtifacts: async () => { throw new Error("not deployed"); },
  });

  reporter.importObject.report_terminal_exception_v1(7, 0, 0);

  assert.equal(reports[0].typeId, 7);
  assert.equal(reports[0].message, null);
  assert.equal(reports[0].stackTrace, null);
  await reporter.drain();
});

test("resolves exact numeric frames from the deployed symbol sidecar", async () => {
  const immediate = [];
  const enriched = [];
  const reporter = createManagedExceptionReporter({
    getMemory: () => new WebAssembly.Memory({ initial: 1 }),
    stringDataOffset: 8,
    reportImmediate: event => immediate.push(event),
    reportEnriched: event => enriched.push(event),
    stackTraceSymbols: [{
      id: 7,
      name: "EntryPoint.Run in Program.cs:line 12",
    }],
    loadArtifacts: async () => {
      throw new Error("not deployed");
    },
  });

  reporter.reportTerminalEvent(
    7,
    "invalid format",
    "at method#7\nat method#8\nnot a managed frame");
  await reporter.drain();

  const expected =
    "at EntryPoint.Run in Program.cs:line 12\nat method#8\nnot a managed frame";
  assert.equal(immediate[0].stackTrace, expected);
  assert.equal(enriched[0].stackTrace, expected);
});

test("rejects malformed stack-trace symbol inputs before reporting", () => {
  for (const stackTraceSymbols of [null, {}, [{ id: 0, name: "A" }],
    [{ id: 1, name: "" }], [{ id: 1, name: "A" }, { id: 1, name: "B" }]]) {
    assert.throws(() => createManagedExceptionReporter({
      getMemory: () => new WebAssembly.Memory({ initial: 1 }),
      stringDataOffset: 8,
      reportImmediate() {},
      reportEnriched() {},
      stackTraceSymbols,
      loadArtifacts: async () => null,
    }), /stack-trace symbol/u);
  }
});

test("drains every scheduled enrichment before host finalization", async () => {
  let resolveArtifacts;
  const artifactsReady = new Promise(resolve => { resolveArtifacts = resolve; });
  const enriched = [];
  const artifacts = { typeSymbols: typeSymbols([typeSymbol(7, "System.FormatException")]) };
  const reporter = createManagedExceptionReporter({
    getMemory: () => new WebAssembly.Memory({ initial: 1 }),
    stringDataOffset: 8,
    reportImmediate: () => {},
    reportEnriched: event => enriched.push(event),
    loadArtifacts: () => artifactsReady,
  });

  reporter.reportTerminalException(7, 0, 0);
  let drained = false;
  const drain = reporter.drain().then(() => { drained = true; });
  await Promise.resolve();
  assert.equal(drained, false);
  resolveArtifacts(artifacts);
  await drain;

  assert.equal(drained, true);
  assert.equal(enriched[0].typeName, "System.FormatException");
});

test("invalid references preserve the type report", () => {
  const memory = new WebAssembly.Memory({ initial: 1 });
  const immediate = [];
  const reporter = createManagedExceptionReporter({
    getMemory: () => memory,
    stringDataOffset: 8,
    reportImmediate: event => immediate.push(event),
    reportEnriched: () => {},
    loadArtifacts: async () => { throw new Error("not deployed"); },
  });

  reporter.reportTerminalException(9, memory.buffer.byteLength, 4);
  assert.equal(immediate[0].typeId, 9);
  assert.equal(immediate[0].message, "<message unavailable>");
});
test("preserves null and empty messages as distinct terminal reports", async () => {
  const memory = new WebAssembly.Memory({ initial: 1 });
  const reports = [];
  const reporter = createManagedExceptionReporter({
    getMemory: () => memory,
    stringDataOffset: 8,
    reportImmediate: report => reports.push(report),
    reportEnriched: () => {},
    loadArtifacts: async () => ({}),
  });

  reporter.reportTerminalException(7, 0, 0);
  reporter.reportTerminalException(8, 16, 0);

  assert.equal(reports[0].message, null);
  assert.equal(reports[1].message, "");
});

test("bounds large messages and refreshes memory after growth", async () => {
  const memory = new WebAssembly.Memory({ initial: 1, maximum: 2 });
  const reports = [];
  const reporter = createManagedExceptionReporter({
    getMemory: () => memory,
    stringDataOffset: 8,
    reportImmediate: report => reports.push(report),
    reportEnriched: () => {},
    loadArtifacts: async () => ({}),
    maximumReportedMessageLength: 4,
  });
  const firstReference = 32;
  const firstView = new DataView(memory.buffer);
  for (const [index, character] of [..."abcdef"].entries()) {
    firstView.setUint16(firstReference + 8 + (index * 2), character.charCodeAt(0), true);
  }

  reporter.reportTerminalException(9, firstReference, 6);
  memory.grow(1);
  const secondReference = 65536 + 32;
  const secondView = new DataView(memory.buffer);
  secondView.setUint16(secondReference + 8, "z".charCodeAt(0), true);
  reporter.reportTerminalException(10, secondReference, 1);

  assert.equal(reports[0].message, "abcd");
  assert.equal(reports[0].messageTruncated, true);
  assert.equal(reports[1].message, "z");
  assert.equal(reports[1].messageTruncated, false);
});

test("preserves an immediate ID and message when artifact loading fails", async () => {
  const immediate = [];
  const enriched = [];
  let resolveEnriched;
  const enrichment = new Promise(resolve => { resolveEnriched = resolve; });
  const reporter = createManagedExceptionReporter({
    getMemory: () => new WebAssembly.Memory({ initial: 1 }),
    stringDataOffset: 8,
    reportImmediate: report => immediate.push(report),
    reportEnriched: report => {
      enriched.push(report);
      resolveEnriched();
    },
    loadArtifacts: async () => { throw new Error("missing diagnostic artifacts"); },
  });

  reporter.reportTerminalException(99, 0, 0);
  await enrichment;

  assert.equal(immediate[0].typeId, 99);
  assert.equal(immediate[0].message, null);
  assert.equal(enriched[0].eventId, immediate[0].eventId);
  assert.equal(enriched[0].typeName, "type#99");
  assert.match(enriched[0].artifactError, /missing diagnostic artifacts/);
});

test("treats non-integral and unsafe message references as unavailable", () => {
  const memory = new WebAssembly.Memory({ initial: 1 });

  assert.equal(copyManagedString(memory, Number.NaN, 1, 8), "<message unavailable>");
  assert.equal(copyManagedString(memory, Number.MAX_SAFE_INTEGER + 1, 1, 8), "<message unavailable>");
  assert.equal(copyManagedString(memory, "16", 1, 8), "<message unavailable>");
});

test("rejects invalid type IDs before host logging", () => {
  const immediate = [];
  const reporter = createManagedExceptionReporter({
    getMemory: () => new WebAssembly.Memory({ initial: 1 }),
    stringDataOffset: 8,
    reportImmediate: event => immediate.push(event),
    reportEnriched: () => {},
    loadArtifacts: () => new Promise(() => {}),
  });

  assert.throws(() => reporter.reportTerminalException(0, 0, 0), RangeError);
  assert.throws(() => reporter.reportTerminalException(0x80000000, 0, 0), RangeError);
  assert.deepEqual(immediate, []);
});

test("rejects invalid event text before host logging", () => {
  const immediate = [];
  const reporter = createManagedExceptionReporter({
    getMemory: () => new WebAssembly.Memory({ initial: 1 }),
    stringDataOffset: 8,
    reportImmediate: event => immediate.push(event),
    reportEnriched() {},
    loadArtifacts: () => new Promise(() => {}),
  });

  assert.throws(() => reporter.reportTerminalEvent(7, 42), /message/);
  assert.throws(() => reporter.reportTerminalEvent(7, "message", 42), /stack trace/);
  assert.deepEqual(immediate, []);
});

test("copies before allowing host callbacks to re-enter the reporter", () => {
  const memory = new WebAssembly.Memory({ initial: 1 });
  new Uint16Array(memory.buffer, 24, 1)[0] = 0x41;
  new Uint16Array(memory.buffer, 40, 1)[0] = 0x42;
  const immediate = [];
  let reporter;
  reporter = createManagedExceptionReporter({
    getMemory: () => memory,
    stringDataOffset: 8,
    reportImmediate: event => {
      immediate.push(event);
      if (event.eventId === 1) {
        new Uint16Array(memory.buffer, 24, 1)[0] = 0x58;
        reporter.reportTerminalException(2, 32, 1);
      }
    },
    reportEnriched: () => {},
    loadArtifacts: () => new Promise(() => {}),
  });

  reporter.reportTerminalException(1, 16, 1);

  assert.deepEqual(immediate.map(event => event.eventId), [1, 2]);
  assert.deepEqual(immediate.map(event => event.message), ["A", "B"]);
});

test("nested host re-entry retains the terminal event for each invocation", () => {
  const memory = new WebAssembly.Memory({ initial: 1 });
  let reporter;
  let nestedEvent;
  reporter = createManagedExceptionReporter({
    getMemory: () => memory,
    stringDataOffset: 0,
    reportImmediate: event => {
      if (event.typeId !== 1) return;
      reporter.reportTerminalException(2, 0, 0);
      nestedEvent = reporter.consumeTerminalEvent();
    },
    reportEnriched: () => {},
    loadArtifacts: () => new Promise(() => {}),
  });

  reporter.reportTerminalException(1, 0, 0);
  const outerEvent = reporter.consumeTerminalEvent();

  assert.equal(nestedEvent.typeId, 2);
  assert.equal(nestedEvent.eventId, 2);
  assert.equal(outerEvent.typeId, 1);
  assert.equal(outerEvent.eventId, 1);
  assert.equal(reporter.consumeTerminalEvent(), undefined);
});

test("rejects a type-symbols sidecar whose entries are not ordered", async () => {
  const outcome = await reportArtifactOutcome({
    typeSymbols: typeSymbols([typeSymbol(2, "Example.Second"), typeSymbol(1, "Example.First")]),
  });

  assert.equal(outcome.typeName, "type#7");
  assert.match(outcome.artifactError, /not in order/);
});

test("keeps host reporting failures distinct from managed terminal failures", () => {
  const hostFailure = new Error("host output failed");
  const reporter = createManagedExceptionReporter({
    getMemory: () => new WebAssembly.Memory({ initial: 1 }),
    stringDataOffset: 8,
    reportImmediate: () => { throw hostFailure; },
    reportEnriched: () => {},
    loadArtifacts: async () => ({}),
  });

  assert.throws(() => reporter.reportTerminalException(3, 0, 0), error => error === hostFailure);
});

test("drain preserves an enrichment output failure", async () => {
  const hostFailure = new Error("host enrichment output failed");
  const reporter = createManagedExceptionReporter({
    getMemory: () => new WebAssembly.Memory({ initial: 1 }),
    stringDataOffset: 8,
    reportImmediate() {},
    reportEnriched: () => { throw hostFailure; },
    loadArtifacts: async () => ({ typeSymbols: typeSymbols([]) }),
  });

  reporter.reportTerminalEvent(7, "message");
  await assert.rejects(() => reporter.drain(), error => error === hostFailure);
});

test("rejects every invalid reporter dependency before accepting reports", () => {
  const valid = {
    getMemory: () => new WebAssembly.Memory({ initial: 1 }),
    stringDataOffset: 8,
    reportImmediate: () => {},
    reportEnriched: () => {},
    loadArtifacts: async () => ({}),
  };
  const invalid = [
    ["getMemory", null, TypeError],
    ["stringDataOffset", -1, RangeError],
    ["stringDataOffset", 1.5, RangeError],
    ["reportImmediate", null, TypeError],
    ["reportEnriched", null, TypeError],
    ["loadArtifacts", null, TypeError],
    ["maximumReportedMessageLength", -1, RangeError],
    ["maximumReportedMessageLength", 1.5, RangeError],
  ];

  for (const [name, value, error] of invalid) {
    assert.throws(() => createManagedExceptionReporter({ ...valid, [name]: value }), error);
  }
});

test("rejects unsafe exception type IDs", () => {
  const reporter = createManagedExceptionReporter({
    getMemory: () => new WebAssembly.Memory({ initial: 1 }),
    stringDataOffset: 8,
    reportImmediate: () => {},
    reportEnriched: () => {},
    loadArtifacts: () => new Promise(() => {}),
  });

  assert.throws(() => reporter.reportTerminalException(Number.NaN, 0, 0), RangeError);
});

test("rejects invalid managed string locations without reading memory", () => {
  const memory = new WebAssembly.Memory({ initial: 1 });

  assert.equal(copyManagedString(null, 16, 1, 8), "<message unavailable>");
  assert.equal(copyManagedString(memory, 16, Number.NaN, 8), "<message unavailable>");
  assert.equal(copyManagedString(memory, 16, -1, 8), "<message unavailable>");
  assert.equal(copyManagedString(memory, 0n, 1, 8), "<message unavailable>");
  assert.equal(copyManagedString(memory, -16, 1, 8), "<message unavailable>");
  assert.equal(copyManagedString(memory, -16n, 1, 8), "<message unavailable>");
  assert.equal(copyManagedString(memory, BigInt(Number.MAX_SAFE_INTEGER), 1, 8), "<message unavailable>");
  assert.equal(copyManagedString(memory, 17, 1, 8), "<message unavailable>");
});

// Without a usable type-symbols sidecar a crash report names the type by its ID, as the program does (issue 225).
test("reports each sidecar failure without losing the event", async () => {
  const valid = typeSymbols([typeSymbol(7, "Example.Error")]);
  const cases = [
    null,
    {},
    { typeSymbols: [1, 2] },
    { typeSymbols: valid.slice(0, 20) },
    { typeSymbols: typeSymbols([], { kind: 2 }) },
  ];

  for (const artifacts of cases) {
    const outcome = await reportArtifactOutcome(artifacts);
    assert.equal(outcome.eventId, 1);
    assert.equal(outcome.typeName, "type#7");
    assert.match(outcome.artifactError, /type-symbols|sidecar/);
  }
});

test("names the type and build from the type-symbols sidecar", async () => {
  const outcome = await reportArtifactOutcome({ typeSymbols: typeSymbols([typeSymbol(7, "Example.Error")]) });

  assert.equal(outcome.artifactError, undefined);
  assert.equal(outcome.typeName, "Example.Error");
  assert.equal(outcome.buildId, buildId);
});

test("enriches an unknown valid exception type by its ID", async () => {
  const outcome = await reportArtifactOutcome({ typeSymbols: typeSymbols([]) });

  assert.equal(outcome.typeName, "type#7");
  assert.equal(outcome.buildId, buildId);
});

test("returns from the raw raise hook so the emitter produces the terminal trap", async () => {
  const reporter = createManagedExceptionReporter({
    getMemory: () => new WebAssembly.Memory({ initial: 1 }),
    stringDataOffset: 8,
    reportImmediate() {},
    reportEnriched() {},
    loadArtifacts: async () => { throw new Error("no artifacts"); },
  });
  reporter.importObject.report_terminal_exception_v2(7, 0, 0, 0, 0);
  assert.equal(reporter.importObject.raise_terminal_exception(), undefined);
  assert.equal(reporter.consumeTerminalEvent().typeId, 7);
  assert.equal(reporter.consumeTerminalEvent(), undefined);
  await reporter.drain();
});
