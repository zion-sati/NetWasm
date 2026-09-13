import assert from "node:assert/strict";
import test from "node:test";

import { createPreview2FilesystemScope } from "./preview2-filesystem-scope.mjs";

class Descriptor {
  openAt() { return new Descriptor(); }
  read() { return [new Uint8Array(), true]; }
  write() { return 0n; }
}

function preopen(overrides = {}) {
  return Object.freeze({
    access: "readOnly", guestPath: "/data", hostPath: "/host/data", ...overrides,
  });
}

test("creates an access-controlled scope and closes decorator before source", () => {
  const calls = [];
  let factoryRequest;
  const root = new Descriptor();
  const filesystem = createPreview2FilesystemScope({
    createFilesystem(request) {
      factoryRequest = request;
      return {
        types: { Descriptor },
        preopens: { getDirectories: () => [[root, "/data"]] },
        dispose() { calls.push("source"); },
      };
    },
    preopens: Object.freeze([preopen()]),
  });
  assert.equal(Object.isFrozen(filesystem), true);
  assert.equal(Object.isFrozen(factoryRequest), true);
  assert.deepEqual({ ...factoryRequest.preopens }, { "/data": "/host/data" });
  const [[descriptor]] = filesystem.preopens.getDirectories();
  assert.throws(() => descriptor.write?.(new Uint8Array(), 0n), value => value === "read-only");
  filesystem.dispose();
  filesystem.dispose();
  assert.deepEqual(calls, ["source"]);
  assert.throws(() => filesystem.preopens.getDirectories(), /disposed/);
});

test("attempts source cleanup after decorator failure and aggregates in order", () => {
  const first = new Error("first");
  const second = new Error("second");
  class FailingDescriptor extends Descriptor {
    openAt() { return new FailingDescriptor(); }
    [Symbol.dispose]() { throw first; }
  }
  const filesystem = createPreview2FilesystemScope({
    createFilesystem: () => ({
      types: { Descriptor: FailingDescriptor },
      preopens: { getDirectories: () => [[new FailingDescriptor(), "/data"]] },
      dispose() { throw second; },
    }),
    preopens: Object.freeze([preopen()]),
  });
  filesystem.preopens.getDirectories()[0][0].openAt({}, "file", {}, {});
  assert.throws(() => filesystem.dispose(), error => error instanceof AggregateError
    && error.errors[0] instanceof AggregateError && error.errors[1] === second);
  filesystem.dispose();
});

test("accepts a source without disposal and rejects malformed requests", () => {
  const valid = {
    createFilesystem: () => ({
      types: { Descriptor }, preopens: { getDirectories: () => [] },
    }),
    preopens: Object.freeze([]),
  };
  createPreview2FilesystemScope(valid).dispose();
  for (const value of [null, 1, [], {}, { ...valid, extra: true }, Object.create(valid)]) {
    assert.throws(() => createPreview2FilesystemScope(value), TypeError);
  }
  assert.throws(() => createPreview2FilesystemScope({ ...valid, createFilesystem: null }), /factory/);
  for (const preopens of [null, [], Object.freeze([null]), Object.freeze([{}]),
    Object.freeze([{ ...preopen(), extra: true }]),
    Object.freeze([preopen({ access: "invalid" })]),
    Object.freeze([preopen(), preopen()])]) {
    assert.throws(() => createPreview2FilesystemScope({ ...valid, preopens }), /preopen/);
  }
  const symbolic = { ...valid, [Symbol("invalid")]: true };
  assert.throws(() => createPreview2FilesystemScope(symbolic), TypeError);
  const accessor = { ...valid };
  Object.defineProperty(accessor, "preopens", { enumerable: true, get: () => valid.preopens });
  assert.throws(() => createPreview2FilesystemScope(accessor), TypeError);
});
