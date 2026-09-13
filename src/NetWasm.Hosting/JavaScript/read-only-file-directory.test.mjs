import assert from "node:assert/strict";
import test from "node:test";
import { createReadOnlyFileDirectory } from "./read-only-file-directory.mjs";

const mutationMethods = [
  "write", "writeViaStream", "appendViaStream", "setSize", "setTimes",
  "createDirectoryAt", "setTimesAt", "linkAt", "removeDirectoryAt",
  "renameAt", "symlinkAt", "unlinkFileAt",
];
const unsupportedMethods = [
  "readViaStream", "advise", "sync", "syncData", "readDirectory", "stat",
  "statAt", "readlinkAt", "metadataHash", "metadataHashAt",
];
const maximumSize = (1n << 64n) - 1n;

test("read-only directory snapshots bytes and returns independent read buffers", () => {
  const bytes = Uint8Array.of(10, 20, 30, 40);
  const mount = createReadOnlyFileDirectory({ fileName: "asset", bytes });
  bytes.fill(0);
  const file = open(mount.directory);
  assert.equal(Object.isFrozen(mount), true);
  assert.equal(Object.isFrozen(mount.directory), true);
  assert.equal(Object.isFrozen(file), true);
  assert.equal(mount.directory.getType(), "directory");
  assert.equal(file.getType(), "regular-file");
  assert.deepEqual(file.getFlags(), { read: true });
  file.getFlags().read = false;
  const [first, eof] = file.read(2n, 1n);
  assert.deepEqual(first, Uint8Array.of(20, 30));
  assert.equal(eof, false);
  first.fill(0);
  assert.deepEqual(file.read(2n, 1n), [Uint8Array.of(20, 30), false]);
  assert.deepEqual(file.read(4n, 0n), [Uint8Array.of(10, 20, 30, 40), true]);
  mount.release();
});

test("reads clamp full u64 lengths and offsets before numeric conversion", () => {
  const mount = fixture();
  const file = open(mount.directory);
  for (const offset of [4n, 5n, BigInt(Number.MAX_SAFE_INTEGER) + 1n, maximumSize]) {
    for (const length of [0n, 1n, maximumSize]) {
      assert.deepEqual(file.read(length, offset), [new Uint8Array(), true]);
    }
  }
  assert.deepEqual(file.read(maximumSize, 0n), [Uint8Array.of(10, 20, 30, 40), true]);
  assert.deepEqual(file.read(maximumSize, 3n), [Uint8Array.of(40), true]);
  assert.deepEqual(file.read(0n, 0n), [new Uint8Array(), false]);
  assert.deepEqual(file.read(1n, 0n), [Uint8Array.of(10), false]);
  for (const value of [0, -1n, maximumSize + 1n, null, "1"]) {
    wasiError(() => file.read(value, 0n), "invalid");
    wasiError(() => file.read(1n, value), "invalid");
  }
  mount.release();
});

test("empty files report EOF for zero and nonzero reads", () => {
  const mount = createReadOnlyFileDirectory({ fileName: "empty", bytes: new Uint8Array() });
  const file = open(mount.directory, "empty");
  assert.deepEqual(file.read(0n, 0n), [new Uint8Array(), true]);
  assert.deepEqual(file.read(1n, 0n), [new Uint8Array(), true]);
  mount.release();
});

test("descriptor disposal is independent and owner release revokes every handle", () => {
  const mount = fixture();
  const directory = mount.directory.openAt({}, ".", { directory: true }, { read: true });
  const first = open(mount.directory);
  const second = open(mount.directory);
  assert.notEqual(first, second);
  mount.directory[Symbol.dispose]();
  mount.directory[Symbol.dispose]();
  wasiError(() => open(mount.directory), "bad-descriptor");
  assert.deepEqual(first.read(1n, 0n), [Uint8Array.of(10), false]);
  const third = open(directory);
  first[Symbol.dispose]();
  wasiError(() => first.read(1n, 0n), "bad-descriptor");
  assert.deepEqual(second.read(1n, 0n), [Uint8Array.of(10), false]);
  mount.release();
  mount.release();
  for (const resource of [directory, second, third]) {
    wasiError(() => resource.getType(), "bad-descriptor");
    wasiError(() => resource.getFlags(), "bad-descriptor");
    wasiError(() => resource.isSameObject(resource), "bad-descriptor");
    wasiError(() => resource.read(1n, 0n), "bad-descriptor");
    wasiError(() => resource.openAt({}, "asset", {}, { read: true }), "bad-descriptor");
    for (const name of [...mutationMethods, ...unsupportedMethods]) {
      wasiError(() => resource[name](), "bad-descriptor");
    }
    resource[Symbol.dispose]();
  }
});

test("simultaneous owners retain distinct files and lifetimes", () => {
  const a = fixture();
  const b = createReadOnlyFileDirectory({ fileName: "asset", bytes: Uint8Array.of(99) });
  const file = open(a.directory);
  assert.equal(file.isSameObject(open(a.directory)), true);
  assert.equal(file.isSameObject(a.directory), false);
  assert.equal(file.isSameObject(open(b.directory)), false);
  assert.equal(a.directory.isSameObject(a.directory.openAt({}, ".", {}, {})), true);
  assert.equal(a.directory.isSameObject(b.directory), false);
  a.release();
  wasiError(() => open(a.directory), "bad-descriptor");
  assert.deepEqual(open(b.directory).read(1n, 0n), [Uint8Array.of(99), true]);
  b.release();
});

test("relative lookup handles directories and does not cross the single-directory boundary", () => {
  const mount = fixture();
  for (const path of ["asset", "./asset", ".//./asset"]) {
    const file = mount.directory.openAt({ symlinkFollow: true }, path,
      { create: false, directory: false, exclusive: false, truncate: false }, { read: true });
    assert.deepEqual(file.read(1n, 0n), [Uint8Array.of(10), false]);
  }
  for (const path of [".", "./", ".//."]) {
    assert.equal(mount.directory.openAt({}, path, {}, {}).getType(), "directory");
  }
  for (const path of ["../asset", "./../asset", "/asset"]) {
    wasiError(() => open(mount.directory, path), "not-permitted");
  }
  for (const path of ["asset/", "asset/.", "asset/..", "asset/other"]) {
    wasiError(() => open(mount.directory, path), "not-directory");
  }
  for (const path of ["", "missing", "ASSET", "missing/asset"]) {
    wasiError(() => open(mount.directory, path), "no-entry");
  }
  for (const path of [null, 1, "asset\0"]) wasiError(() => open(mount.directory, path), "invalid");
  wasiError(() => mount.directory.openAt({}, "asset", { directory: true }, {}), "not-directory");
  wasiError(() => open(open(mount.directory)), "not-directory");
  wasiError(() => mount.directory.read(1n, 0n), "is-directory");
  mount.release();
});

test("opening without read authority does not grant reads", () => {
  const mount = fixture();
  for (const flags of [{}, { read: false }]) {
    const file = mount.directory.openAt({}, "asset", {}, flags);
    assert.deepEqual(file.getFlags(), { read: false });
    wasiError(() => file.read(1n, 0n), "bad-descriptor");
  }
  mount.release();
});

test("creation and write flags fail before lookup or handle acquisition", () => {
  const mount = fixture();
  for (const openFlags of [{ create: true }, { truncate: true }]) {
    wasiError(() => mount.directory.openAt({}, "missing", openFlags, {}), "read-only");
  }
  for (const flags of [{ write: true }, { mutateDirectory: true }]) {
    wasiError(() => mount.directory.openAt({}, "missing", {}, flags), "read-only");
  }
  wasiError(() => mount.directory.openAt({}, "asset", { exclusive: true }, {}), "exist");
  wasiError(() => mount.directory.openAt({}, "missing", { exclusive: true }, {}), "no-entry");
  for (const name of ["fileIntegritySync", "dataIntegritySync", "requestedWriteSync"]) {
    const file = mount.directory.openAt({}, "asset", {}, { read: true, [name]: true });
    assert.deepEqual(file.getFlags(), { read: true });
    assert.deepEqual(file.read(1n, 0n), [Uint8Array.of(10), false]);
  }
  assert.deepEqual(open(mount.directory).read(1n, 0n), [Uint8Array.of(10), false]);
  mount.release();
});

test("all mutation entry points reject without changing the file", () => {
  const mount = fixture();
  const file = open(mount.directory);
  for (const resource of [mount.directory, file]) {
    for (const name of mutationMethods) wasiError(() => resource[name](), "read-only");
  }
  assert.deepEqual(file.read(4n, 0n), [Uint8Array.of(10, 20, 30, 40), true]);
  mount.release();
});

test("operations outside the internal asset contract fail explicitly", () => {
  const mount = fixture();
  for (const resource of [mount.directory, open(mount.directory)]) {
    for (const name of unsupportedMethods) wasiError(() => resource[name](), "unsupported");
  }
  mount.release();
});

test("invalid flag values fail as WASI invalid arguments", () => {
  const mount = fixture();
  for (const value of [null, 0, [], { unknown: true }, { read: "true" }]) {
    wasiError(() => mount.directory.openAt(value, "asset", {}, {}), "invalid");
    wasiError(() => mount.directory.openAt({}, "asset", value, {}), "invalid");
    wasiError(() => mount.directory.openAt({}, "asset", {}, value), "invalid");
  }
  wasiError(() => mount.directory.openAt({ symlinkFollow: 1 }, "asset", {}, {}), "invalid");
  wasiError(() => mount.directory.openAt({}, "asset", { directory: 1 }, {}), "invalid");
  mount.release();
});

test("invalid construction never produces a resource", () => {
  for (const options of [undefined, null, 0, [], {}]) {
    assert.throws(() => createReadOnlyFileDirectory(options), TypeError);
  }
  for (const fileName of [null, 1, "", ".", "..", "a/b", "a\\b", "a\0b"]) {
    assert.throws(() => createReadOnlyFileDirectory({ fileName, bytes: new Uint8Array() }), TypeError);
  }
  for (const bytes of [null, [], new ArrayBuffer(1), "bytes"]) {
    assert.throws(() => createReadOnlyFileDirectory({ fileName: "asset", bytes }), TypeError);
  }
});

function fixture() {
  return createReadOnlyFileDirectory({ fileName: "asset", bytes: Uint8Array.of(10, 20, 30, 40) });
}

function open(directory, path = "asset") {
  return directory.openAt({}, path, {}, { read: true });
}

function wasiError(action, code) {
  assert.throws(action, value => value === code);
}
