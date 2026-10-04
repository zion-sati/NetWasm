import assert from "node:assert/strict";
import test from "node:test";
import {
  createComponentManagedExceptionHost,
} from "./component-managed-exception-host.mjs";

test("default reporting preserves the event when diagnostic artifacts are absent", async () => {
  const host = createComponentManagedExceptionHost({});
  host.imports.report(7, undefined, undefined);

  await host.drain();

  assert.deepEqual(host.consumeTerminalEvent(), {
    eventId: 1, typeId: 7, message: null, messageTruncated: false, stackTrace: null,
  });
  assert.equal(host.consumeTerminalEvent(), undefined);
});

test("forwards lazy artifact loading and the caller's message length limit", async () => {
  let loads = 0;
  const reports = [];
  const host = createComponentManagedExceptionHost({
    diagnosticArtifacts: async () => {
      loads++;
      throw new Error("fixture artifacts unavailable");
    },
    managedExceptionReporting: {
      maximumMessageLength: 1,
      reportEnriched: event => reports.push(event),
    },
  });
  host.imports.report(7, [0x41, 0x42], undefined);

  await host.drain();

  assert.equal(loads, 1);
  assert.equal(host.consumeTerminalEvent().messageTruncated, true);
  assert.equal(reports.length, 1);
  assert.equal(reports[0].message, "A");
  assert.match(reports[0].artifactError, /fixture artifacts unavailable/u);
});

test("copies component UTF-16 payloads and preserves null and empty messages", () => {
  const reports = [];
  const host = createComponentManagedExceptionHost({
    diagnosticArtifacts: {},
    managedExceptionReporting: {
      reportImmediate: event => reports.push(event),
      reportEnriched() {},
    },
  });

  host.imports.report(1, null, null);
  host.imports.report(2, new Uint16Array(), new Uint16Array());
  host.imports.report(3, [0x41, 0xd800, 0x42], [0x61, 0x74]);
  host.imports.report(4, undefined, undefined);
  host.imports.report(5, [0x41], undefined);
  host.imports.report(6, undefined, [0x61, 0x74]);

  assert.deepEqual(reports.map(event => event.message), [null, "", "A\ud800B", null, "A", null]);
  assert.deepEqual(reports.map(event => event.stackTrace), [null, "", "at", null, null, "at"]);
  assert.equal(host.consumeTerminalEvent().typeId, 6);
  assert.equal(host.consumeTerminalEvent().typeId, 5);
  assert.equal(host.consumeTerminalEvent().typeId, 4);
  assert.equal(host.consumeTerminalEvent().typeId, 3);
  assert.equal(host.consumeTerminalEvent().typeId, 2);
  assert.equal(host.consumeTerminalEvent().typeId, 1);
});

test("rejects malformed component terminal payloads", () => {
  const host = createComponentManagedExceptionHost({
    diagnosticArtifacts: {},
    managedExceptionReporting: {
      reportImmediate() {},
      reportEnriched() {},
    },
  });

  for (const value of ["message", Uint8Array.of(1), [0x10000], [-1], [1.5]]) {
    assert.throws(() => host.imports.report(1, value, null), /message/u);
    assert.throws(() => host.imports.report(1, null, value), /stack trace/u);
  }
});
