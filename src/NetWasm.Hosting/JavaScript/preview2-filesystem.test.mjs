import assert from "node:assert/strict";
import test from "node:test";
import { createPreview2Filesystem } from "./preview2-filesystem.mjs";

const ordinaryMethods = [
  "readViaStream", "writeViaStream", "appendViaStream", "advise", "syncData",
  "getFlags", "getType", "setSize", "setTimes", "read", "write", "readDirectory",
  "sync", "createDirectoryAt", "stat", "statAt", "setTimesAt", "readlinkAt",
  "removeDirectoryAt", "symlinkAt", "unlinkFileAt", "metadataHash", "metadataHashAt",
];

test("snapshots preopens and preserves source namespaces without mutation", () => {
  const source = fixture();
  const fs = createPreview2Filesystem({ filesystem: source.filesystem });
  source.entries.push([new source.Descriptor("later"), "/later"]);
  const entries = fs.preopens.getDirectories();
  assert.deepEqual(entries.map(([, path]) => path), ["/consumer"]);
  assert.ok(entries[0][0] instanceof fs.types.Descriptor);
  assert.notEqual(fs.types.Descriptor, source.Descriptor);
  assert.equal(fs.types.DirectoryEntryStream, source.filesystem.types.DirectoryEntryStream);
  assert.equal(fs.types.filesystemErrorCode("error"), "error-code");
  assert.deepEqual(source.calls.at(-1), [source.filesystem.types, "error"]);
  assert.equal(Object.isFrozen(fs), true);
  assert.equal(Object.isFrozen(fs.types), true);
  assert.equal(Object.isFrozen(fs.preopens), true);
  assert.equal(Object.isFrozen(fs.types.Descriptor.prototype), true);
  assert.equal(Object.isFrozen(source.Descriptor.prototype), false);
  entries[0][1] = "/changed";
  entries.length = 0;
  assert.equal(fs.preopens.getDirectories()[0][1], "/consumer");
  const firstRoot = fs.preopens.getDirectories()[0][0];
  const secondRoot = fs.preopens.getDirectories()[0][0];
  assert.notEqual(firstRoot, secondRoot);
  assert.equal(firstRoot.isSameObject(secondRoot), true);
  firstRoot[Symbol.dispose]();
  assert.equal(secondRoot.read(1n, 0n), source.result);
  fs.dispose();
  assert.equal(source.root.closes, 0);
  assert.equal(source.disposals, 0);
});

test("forwards ordinary descriptor methods with the original receiver and arguments", () => {
  const source = fixture();
  const fs = createPreview2Filesystem({ filesystem: source.filesystem });
  const [[root]] = fs.preopens.getDirectories();
  for (const name of ordinaryMethods) {
    assert.equal(root[name](1, 2, 3), source.result);
    assert.deepEqual(source.calls.at(-1), [source.root, name, 1, 2, 3]);
  }
  fs.dispose();
});

test("enforces read-only access across opened descriptors and mutation paths", () => {
  const source = fixture();
  source.root.getFlags = () => ({ read: true, write: true, mutateDirectory: true });
  const access = Object.freeze([
    Object.freeze({ guestPath: "/consumer", access: "readOnly" }),
  ]);
  const fs = createPreview2Filesystem({ filesystem: source.filesystem, preopenAccess: access });
  const [[root]] = fs.preopens.getDirectories();
  assert.deepEqual(root.getFlags(), { read: true, write: false, mutateDirectory: false });
  const file = root.openAt({}, "file", {}, { read: true });
  assert.equal(file.read(1n, 0n), source.result);
  assert.ok(root.openAt({}, "file", null, null) instanceof fs.types.Descriptor);
  wasiError(() => root.linkAt({}, "old", root, "new"), "read-only");
  wasiError(() => root.renameAt("old", root, "new"), "read-only");
  for (const name of mutatingMethods()) {
    wasiError(() => file[name](1, 2, 3), "read-only");
  }
  for (const [openFlags, flags] of [
    [{ create: true }, {}], [{ truncate: true }, {}], [{}, { write: true }],
    [{}, { mutateDirectory: true }],
  ]) {
    wasiError(() => root.openAt({}, "file", openFlags, flags), "read-only");
  }
  const readWriteSource = fixture();
  const readWrite = createPreview2Filesystem({
    filesystem: readWriteSource.filesystem,
    preopenAccess: Object.freeze([
      Object.freeze({ guestPath: "/consumer", access: "readWrite" }),
    ]),
  });
  const [[writable]] = readWrite.preopens.getDirectories();
  wasiError(() => root.linkAt({}, "old", writable, "new"), "bad-descriptor");
  fs.dispose();
  readWrite.dispose();
});

test("requires immutable access grants to match every source preopen", () => {
  const source = fixture();
  for (const preopenAccess of [null, [], Object.freeze([null]), Object.freeze([
    Object.freeze({ guestPath: "/consumer", access: "invalid" }),
  ]), Object.freeze([
    Object.freeze({ guestPath: "/other", access: "readOnly" }),
  ]), Object.freeze([
    Object.freeze({ guestPath: "/consumer", access: "readOnly" }),
    Object.freeze({ guestPath: "/consumer", access: "readWrite" }),
  ])]) {
    assert.throws(() => createPreview2Filesystem({
      filesystem: source.filesystem, preopenAccess,
    }), /preopen/);
  }
  assert.throws(() => createPreview2Filesystem({
    filesystem: source.filesystem,
    preopenAccess: Object.freeze([
      Object.freeze({ guestPath: "/consumer", access: "readOnly" }),
      Object.freeze({ guestPath: "/extra", access: "readOnly" }),
    ]),
  }), /preopen/);
});

test("wraps opened descriptors and unwraps borrowed descriptor arguments", () => {
  const source = fixture();
  const fs = createPreview2Filesystem({ filesystem: source.filesystem });
  const [[root]] = fs.preopens.getDirectories();
  const file = root.openAt({}, "file", {}, { read: true });
  assert.ok(file instanceof fs.types.Descriptor);
  assert.deepEqual(source.calls.at(-1), [source.root, "openAt", {}, "file", {}, { read: true }]);
  assert.equal(file.read(1n, 0n), source.result);
  assert.equal(file.isSameObject(file), true);
  assert.deepEqual(source.calls.at(-1), [source.opened[0], "isSameObject", source.opened[0]]);
  assert.equal(root.isSameObject(file), false);
  assert.equal(root.linkAt({}, "old", file, "new"), source.result);
  assert.deepEqual(source.calls.at(-1), [source.root, "linkAt", {}, "old", source.opened[0], "new"]);
  assert.equal(root.renameAt("old", file, "new"), source.result);
  assert.deepEqual(source.calls.at(-1), [source.root, "renameAt", "old", source.opened[0], "new"]);
  file[Symbol.dispose]();
  file[Symbol.dispose]();
  assert.equal(source.opened[0].closes, 1);
  fs.dispose();
  assert.equal(source.opened[0].closes, 1);
});

test("one descriptor class covers the internal file and consumer preopens", () => {
  const source = fixture();
  const fs = createPreview2Filesystem({ filesystem: source.filesystem });
  const bytes = Uint8Array.of(10, 20, 30);
  const release = fs.mountReadOnlyFile({ guestPath: "/internal/asset", bytes });
  bytes.fill(0);
  const [[consumer], [internal, path]] = fs.preopens.getDirectories();
  assert.equal(path, "/internal");
  const file = internal.openAt({}, "asset", {}, { read: true });
  assert.ok(consumer instanceof fs.types.Descriptor);
  assert.ok(file instanceof fs.types.Descriptor);
  assert.deepEqual(file.read(2n, 1n), [Uint8Array.of(20, 30), true]);
  assert.equal(file.isSameObject(internal.openAt({}, "asset", {}, { read: true })), true);
  assert.equal(file.isSameObject(consumer), false);
  assert.equal(consumer.isSameObject(file), false);
  const calls = source.calls.length;
  for (const [a, b] of [[consumer, internal], [internal, consumer], [internal, internal]]) {
    wasiError(() => a.linkAt({}, "old", b, "new"), "read-only");
    wasiError(() => a.renameAt("old", b, "new"), "read-only");
  }
  wasiError(() => file.write(Uint8Array.of(0), 0n), "read-only");
  assert.equal(source.calls.length, calls);
  release();
  release();
  wasiError(() => file.read(1n, 0n), "bad-descriptor");
  assert.deepEqual(fs.preopens.getDirectories().map(([, name]) => name), ["/consumer"]);
  assert.equal(consumer.read(1n, 0n), source.result);
  fs.dispose();
});

test("dropping a borrowed root permits later enumeration without closing its backing", () => {
  const source = fixture();
  const fs = createPreview2Filesystem({ filesystem: source.filesystem });
  const [[first]] = fs.preopens.getDirectories();
  first[Symbol.dispose]();
  wasiError(() => first.read(1n, 0n), "bad-descriptor");
  const [[second]] = fs.preopens.getDirectories();
  assert.notEqual(first, second);
  assert.equal(second.read(1n, 0n), source.result);
  assert.equal(source.root.closes, 0);
  fs.dispose();
});

test("scope disposal closes owned opens and internal mounts but not caller resources", () => {
  const source = fixture();
  const fs = createPreview2Filesystem({ filesystem: source.filesystem });
  const release = fs.mountReadOnlyFile({ guestPath: "/internal/asset", bytes: Uint8Array.of(7) });
  const [[consumer], [internal]] = fs.preopens.getDirectories();
  const a = consumer.openAt({}, "a", {}, {});
  const b = consumer.openAt({}, "b", {}, {});
  const c = internal.openAt({}, "asset", {}, { read: true });
  fs.dispose();
  fs.dispose();
  release();
  assert.deepEqual(source.opened.map(file => file.closes), [1, 1]);
  assert.equal(source.root.closes, 0);
  assert.equal(source.disposals, 0);
  for (const descriptor of [consumer, a, b, c]) wasiError(() => descriptor.read(1n, 0n), "bad-descriptor");
  assert.throws(() => fs.preopens.getDirectories(), /disposed/);
  assert.throws(() => fs.mountReadOnlyFile({ guestPath: "/another/asset", bytes: new Uint8Array() }), /closed/);
});

test("cleanup attempts every descriptor once even when disposal fails", () => {
  const source = fixture();
  const fs = createPreview2Filesystem({ filesystem: source.filesystem });
  const [[root]] = fs.preopens.getDirectories();
  const a = root.openAt({}, "a", {}, {});
  root.openAt({}, "b", {}, {});
  const firstError = new Error("first");
  const secondError = new Error("second");
  source.opened[0].closeError = firstError;
  source.opened[1].closeError = secondError;
  assert.throws(() => fs.dispose(), error => error instanceof AggregateError
    && error.errors[0] === firstError && error.errors[1] === secondError);
  fs.dispose();
  a[Symbol.dispose]();
  assert.deepEqual(source.opened.map(file => file.closes), [1, 1]);
});

test("mount collisions are rejected and release frees a directory before setup is sealed", () => {
  const source = fixture();
  const fs = createPreview2Filesystem({ filesystem: source.filesystem });
  assert.throws(() => fs.mountReadOnlyFile({ guestPath: "/consumer/asset", bytes: new Uint8Array() }), /collides/);
  const release = fs.mountReadOnlyFile({ guestPath: "/internal/asset", bytes: Uint8Array.of(1) });
  assert.throws(() => fs.mountReadOnlyFile({ guestPath: "/internal/other", bytes: new Uint8Array() }), /collides/);
  release();
  fs.mountReadOnlyFile({ guestPath: "/internal/asset", bytes: Uint8Array.of(2) });
  fs.mountReadOnlyFile({ guestPath: "/root-asset", bytes: Uint8Array.of(3) });
  const entries = fs.preopens.getDirectories();
  assert.deepEqual(entries.map(([, path]) => path), ["/consumer", "/internal", "/"]);
  assert.deepEqual(entries[1][0].openAt({}, "asset", {}, { read: true }).read(1n, 0n), [Uint8Array.of(2), true]);
  assert.throws(() => fs.mountReadOnlyFile({ guestPath: "/later/asset", bytes: new Uint8Array() }), /closed/);
  fs.dispose();
});

test("independent scopes cannot exchange descriptors or observe each other's mounts", () => {
  const source = fixture();
  const a = createPreview2Filesystem({ filesystem: source.filesystem });
  const b = createPreview2Filesystem({ filesystem: source.filesystem });
  a.mountReadOnlyFile({ guestPath: "/internal/asset", bytes: Uint8Array.of(1) });
  const [[aRoot]] = a.preopens.getDirectories();
  const [[bRoot]] = b.preopens.getDirectories();
  assert.equal(b.preopens.getDirectories().length, 1);
  wasiError(() => aRoot.isSameObject(bRoot), "bad-descriptor");
  assert.throws(() => new a.types.Descriptor(), TypeError);
  wasiError(() => a.types.Descriptor.prototype.read.call({}, 1n, 0n), "bad-descriptor");
  a.dispose();
  assert.equal(bRoot.read(1n, 0n), source.result);
  b.dispose();
});

test("partial providers retain only their supplied resource operations", () => {
  class Descriptor {
    openAt() { return new Descriptor(); }
    read() { return [Uint8Array.of(1), true]; }
    futureOperation() { throw new Error("must not be exposed"); }
  }
  const fs = createPreview2Filesystem({ filesystem: {
    types: { Descriptor }, preopens: { getDirectories: () => [[new Descriptor(), "/"]] },
  } });
  assert.equal(fs.types.Descriptor.prototype.futureOperation, undefined);
  assert.equal(fs.types.Descriptor.prototype.write, undefined);
  const [[root]] = fs.preopens.getDirectories();
  const file = root.openAt();
  assert.deepEqual(file.read(), [Uint8Array.of(1), true]);
  fs.dispose();
});

test("invalid provider inputs and preopen inventories fail before any mount", () => {
  class MissingRead { openAt() {} }
  class MissingOpen { read() {} }
  for (const filesystem of [undefined, {}, { types: { Descriptor: () => {} } },
    { types: { Descriptor: MissingRead } }, { types: { Descriptor: MissingOpen } }]) {
    assert.throws(() => createPreview2Filesystem({ filesystem }), TypeError);
  }
  assert.throws(() => createPreview2Filesystem(), TypeError);
  const source = fixture();
  const absentPreopens = { types: source.filesystem.types };
  assert.throws(() => createPreview2Filesystem({ filesystem: absentPreopens }), TypeError);
  for (const entries of [null, [null], [[source.root]], [[source.root, "/x", "extra"]],
    [[source.root, "/x"], [source.root, "/x"]], [[{}, "/x"]]]) {
    assert.throws(() => createPreview2Filesystem({ filesystem: {
      types: source.filesystem.types, preopens: { getDirectories: () => entries },
    } }), TypeError);
  }
  source.root.read = null;
  assert.throws(() => createPreview2Filesystem({ filesystem: source.filesystem }), /incomplete/);
  assert.equal(source.root.closes, 0);
});

test("invalid opened descriptors are rejected and released when possible", () => {
  const source = fixture();
  const fs = createPreview2Filesystem({ filesystem: source.filesystem });
  const [[root]] = fs.preopens.getDirectories();
  for (const value of [null, 1, {}]) {
    source.next = () => value;
    assert.throws(() => root.openAt(), /invalid descriptor/);
  }
  const incomplete = new source.Descriptor("bad");
  incomplete.read = null;
  source.next = () => incomplete;
  assert.throws(() => root.openAt(), /incomplete/);
  assert.equal(incomplete.closes, 1);
  incomplete.closeError = new Error("cleanup");
  assert.throws(() => root.openAt(), error => error instanceof AggregateError
    && error.errors[1] === incomplete.closeError);
  const invalidRelease = new source.Descriptor("bad-release");
  invalidRelease[Symbol.dispose] = 1;
  source.next = () => invalidRelease;
  assert.throws(() => root.openAt(), AggregateError);
  fs.dispose();
});

test("canonical WASI paths and byte contents are required for mounts", () => {
  const source = fixture();
  const fs = createPreview2Filesystem({ filesystem: source.filesystem });
  for (const guestPath of [undefined, 1, "asset", "/", "/a/", "/a//b", "/a/./b", "/a/../b", "/a\\b", "/a\0b"]) {
    assert.throws(() => fs.mountReadOnlyFile({ guestPath, bytes: new Uint8Array() }), TypeError);
  }
  assert.throws(() => fs.mountReadOnlyFile(), TypeError);
  assert.throws(() => fs.mountReadOnlyFile({ guestPath: "/a/file", bytes: null }), TypeError);
  assert.equal(fs.preopens.getDirectories().length, 1);
  fs.dispose();
  for (const path of ["relative", "/a/", "/a/./b"]) {
    source.entries[0][1] = path;
    assert.throws(() => createPreview2Filesystem({ filesystem: source.filesystem }), TypeError);
  }
});

function fixture() {
  const source = { calls: [], opened: [], result: Object.freeze({ result: true }), disposals: 0, next: null };
  class Descriptor {
    constructor(identity) { this.identity = identity; this.closes = 0; }
    openAt(...args) {
      source.calls.push([this, "openAt", ...args]);
      const file = source.next ? source.next() : new Descriptor("file");
      source.opened.push(file);
      return file;
    }
    isSameObject(other) {
      source.calls.push([this, "isSameObject", other]);
      return this.identity === other.identity;
    }
    linkAt(...args) { source.calls.push([this, "linkAt", ...args]); return source.result; }
    renameAt(...args) { source.calls.push([this, "renameAt", ...args]); return source.result; }
    [Symbol.dispose]() { this.closes++; if (this.closeError) throw this.closeError; }
  }
  for (const name of ordinaryMethods) {
    Descriptor.prototype[name] = function (...args) {
      source.calls.push([this, name, ...args]);
      return source.result;
    };
  }
  source.Descriptor = Descriptor;
  source.root = new Descriptor("root");
  source.entries = [[source.root, "/consumer"]];
  source.filesystem = {
    types: {
      Descriptor,
      DirectoryEntryStream: class DirectoryEntryStream {},
      filesystemErrorCode(error) { source.calls.push([this, error]); return "error-code"; },
    },
    preopens: { getDirectories() { assert.equal(this, source.filesystem.preopens); return source.entries; } },
    dispose() { source.disposals++; },
  };
  return source;
}

function mutatingMethods() {
  return [
    "writeViaStream", "appendViaStream", "setSize", "setTimes", "write",
    "createDirectoryAt", "setTimesAt", "removeDirectoryAt", "symlinkAt", "unlinkFileAt",
  ];
}

function wasiError(action, code) { assert.throws(action, value => value === code); }
