import assert from "node:assert/strict";
import { createHash, webcrypto } from "node:crypto";
import test from "node:test";
import { createManagedExceptionReporter, copyManagedString } from "./managed-exception-reporter.mjs";

const wasmBytes = Uint8Array.from([0, 97, 115, 109]);
const digest = bytes => createHash("sha256").update(bytes).digest("hex");

function diagnosticArtifacts(map, overrides = {}) {
  const mapBytes = new TextEncoder().encode(
    typeof map === "string" ? map : JSON.stringify(map));
  return {
    manifest: {
      schemaVersion: 1,
      buildId: "build",
      wasmSha256: digest(wasmBytes),
      exceptionTypeMapSha256: digest(mapBytes),
    },
    wasmBytes,
    mapBytes,
    ...overrides,
  };
}

async function reportArtifactOutcome(artifacts, crypto = webcrypto) {
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
    crypto,
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
  const wasmBytes = Uint8Array.from([0, 97, 115, 109]);
  const mapBytes = new TextEncoder().encode(JSON.stringify({ schemaVersion: 2, buildId: "build", entries: [{ typeId: 7, displayName: "BoomException", canonicalIdentity: "BoomException, App", assemblyIdentity: "App" }] }));
  const digest = bytes => createHash("sha256").update(bytes).digest("hex");
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
    crypto: webcrypto,
    loadArtifacts: async () => {
      loads++;
      return { manifest: { schemaVersion: 1, buildId: "build", wasmSha256: digest(wasmBytes), exceptionTypeMapSha256: digest(mapBytes) }, wasmBytes, mapBytes };
    },
  });

  reporter.reportTerminalException(7, 32, 2);
  reporter.reportTerminalException(7, 32, 2);
  assert.deepEqual(immediate.map(event => event.message), ["hi", "hi"]);
  assert.deepEqual(immediate.map(event => event.eventId), [1, 2]);
  await enrichments;
  assert.equal(loads, 1);
  assert.deepEqual(enriched.map(event => event.typeName), ["BoomException", "BoomException"]);
});

test("invalid references preserve the type report", () => {
  const memory = new WebAssembly.Memory({ initial: 1 });
  const immediate = [];
  const reporter = createManagedExceptionReporter({
    getMemory: () => memory,
    stringDataOffset: 8,
    reportImmediate: event => immediate.push(event),
    reportEnriched: () => {},
    crypto: webcrypto,
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
  assert.equal(enriched[0].typeName, "<exception type #99 unavailable>");
  assert.match(enriched[0].artifactError, /missing diagnostic artifacts/);
});

test("preserves the immediate report when artifact binding is mismatched", async () => {
  const wasmBytes = Uint8Array.from([0, 97, 115, 109]);
  const mapBytes = new TextEncoder().encode(JSON.stringify({
    schemaVersion: 2,
    buildId: "build",
    entries: [],
  }));
  const digest = bytes => createHash("sha256").update(bytes).digest("hex");
  const immediate = [];
  const enriched = [];
  let resolveEnriched;
  const enrichment = new Promise(resolve => { resolveEnriched = resolve; });
  const reporter = createManagedExceptionReporter({
    getMemory: () => new WebAssembly.Memory({ initial: 1 }),
    stringDataOffset: 8,
    reportImmediate: event => immediate.push(event),
    reportEnriched: event => {
      enriched.push(event);
      resolveEnriched();
    },
    crypto: webcrypto,
    loadArtifacts: async () => ({
      manifest: {
        schemaVersion: 1,
        buildId: "build",
        wasmSha256: "mismatched",
        exceptionTypeMapSha256: digest(mapBytes),
      },
      wasmBytes,
      mapBytes,
    }),
  });

  reporter.reportTerminalException(7, 0, 0);
  await enrichment;

  assert.equal(immediate[0].typeId, 7);
  assert.equal(enriched[0].eventId, immediate[0].eventId);
  assert.match(enriched[0].artifactError, /digest mismatch/);
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

test("rejects exception maps whose entries are not deterministically ordered", async () => {
  const memory = new WebAssembly.Memory({ initial: 1 });
  const wasmBytes = Uint8Array.from([0, 97, 115, 109]);
  const mapBytes = new TextEncoder().encode(JSON.stringify({
    schemaVersion: 2,
    buildId: "build",
    entries: [
      { typeId: 2, displayName: "Example.Second", canonicalIdentity: "Example.Second, Example", assemblyIdentity: "Example" },
      { typeId: 1, displayName: "Example.First", canonicalIdentity: "Example.First, Example", assemblyIdentity: "Example" },
    ],
  }));
  const digest = bytes => createHash("sha256").update(bytes).digest("hex");
  const enriched = [];
  let resolveEnriched;
  const enrichment = new Promise(resolve => { resolveEnriched = resolve; });
  const reporter = createManagedExceptionReporter({
    getMemory: () => memory,
    stringDataOffset: 8,
    reportImmediate: () => {},
    reportEnriched: event => {
      enriched.push(event);
      resolveEnriched();
    },
    crypto: webcrypto,
    loadArtifacts: async () => ({
      manifest: {
        schemaVersion: 1,
        buildId: "build",
        wasmSha256: digest(wasmBytes),
        exceptionTypeMapSha256: digest(mapBytes),
      },
      wasmBytes,
      mapBytes,
    }),
  });

  reporter.reportTerminalException(2, 0, 0);
  await enrichment;

  assert.equal(enriched.length, 1);
  assert.match(enriched[0].artifactError, /ordered by ascending type ID/);
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

test("reports each diagnostic artifact envelope failure without losing the event", async () => {
  const validMap = { schemaVersion: 2, buildId: "build", entries: [] };
  const valid = diagnosticArtifacts(validMap);
  const cases = [
    { ...valid, manifest: null },
    { ...valid, manifest: { ...valid.manifest, schemaVersion: 2 } },
    { ...valid, manifest: { ...valid.manifest, buildId: 42 } },
    { ...valid, mapBytes: null },
    { ...valid, wasmBytes: null },
    { ...valid, manifest: { ...valid.manifest, exceptionTypeMapSha256: "0".repeat(64) } },
    { ...valid, manifest: { ...valid.manifest, wasmSha256: "0".repeat(64) } },
  ];

  for (const artifacts of cases) {
    const outcome = await reportArtifactOutcome(artifacts);
    assert.equal(outcome.eventId, 1);
    assert.match(outcome.artifactError, /artifact|digest/);
  }
  const noCrypto = await reportArtifactOutcome(valid, {});
  assert.match(noCrypto.artifactError, /cryptographic digest support/);
});

test("reports each exception-map contract failure without losing the event", async () => {
  const entry = {
    typeId: 7,
    displayName: "Example.Error",
    canonicalIdentity: "Example.Error, Example",
    assemblyIdentity: "Example",
  };
  const maps = [
    "{",
    { schemaVersion: 1, buildId: "build", entries: [] },
    { schemaVersion: 2, buildId: "other", entries: [] },
    { schemaVersion: 2, buildId: "build", entries: null },
    { schemaVersion: 2, buildId: "build", entries: [{ ...entry, typeId: Number.NaN }] },
    { schemaVersion: 2, buildId: "build", entries: [{ ...entry, displayName: 7 }] },
    { schemaVersion: 2, buildId: "build", entries: [{ ...entry, displayName: "" }] },
    { schemaVersion: 2, buildId: "build", entries: [{ ...entry, canonicalIdentity: 7 }] },
    { schemaVersion: 2, buildId: "build", entries: [{ ...entry, canonicalIdentity: "" }] },
    { schemaVersion: 2, buildId: "build", entries: [{ ...entry, assemblyIdentity: 7 }] },
    { schemaVersion: 2, buildId: "build", entries: [{ ...entry, assemblyIdentity: "" }] },
  ];

  for (const map of maps) {
    const outcome = await reportArtifactOutcome(diagnosticArtifacts(map));
    assert.equal(outcome.eventId, 1);
    assert.match(outcome.artifactError, /JSON|exception type map|entry|ordered/);
  }

  const mediaType = await reportArtifactOutcome(diagnosticArtifacts(
    { schemaVersion: 2, buildId: "build", entries: [] },
    { mapMediaType: "application/json" }));
  assert.match(mediaType.artifactError, /media type/);
});

test("enriches an unknown valid exception type without fabricating identity", async () => {
  const outcome = await reportArtifactOutcome(diagnosticArtifacts({
    schemaVersion: 2,
    buildId: "build",
    entries: [],
  }, { mapMediaType: "application/vnd.netwasm.exception-types+json;version=2" }));

  assert.equal(outcome.typeName, "<unknown exception type #7>");
  assert.equal(outcome.canonicalIdentity, undefined);
  assert.equal(outcome.assemblyIdentity, undefined);
  assert.equal(outcome.buildId, "build");
});
