import assert from "node:assert/strict";
import {
  closeSync, ftruncateSync, lstatSync, mkdirSync, openSync, readdirSync, readlinkSync, readSync, realpathSync,
  renameSync, rmdirSync, statSync, symlinkSync, unlinkSync, utimesSync, writeSync,
} from "node:fs";
import { mkdir, mkdtemp, readFile, rm, stat, symlink, writeFile } from "node:fs/promises";
import { tmpdir } from "node:os";
import { join } from "node:path";
import test from "node:test";

import { readSyncFuture, syncDrainKey } from "./sync-values.mjs";
import { createWasiFilesystem } from "./wasi-filesystem.mjs";

const hostFiles = Object.freeze({
  closeSync, ftruncateSync, lstatSync, mkdirSync, openSync, readdirSync, readlinkSync, readSync, realpathSync,
  renameSync, rmdirSync, statSync, symlinkSync, unlinkSync, utimesSync, writeSync,
});
const unused = () => { throw new Error("not called"); };

// A fake host file system: the actions a test gives, the rest unused.
function fakeHost(actions) {
  return Object.freeze({
    ...Object.fromEntries(Object.keys(hostFiles).map(name => [name, unused])),
    ...actions,
  });
}

function stream(chunks) {
  return { read: async () => (chunks.length === 0 ? { done: true } : { done: false, value: chunks.shift() }) };
}
const noFlags = Object.freeze({});
const read = Object.freeze({ read: true });

async function readAll([stream, future]) {
  const chunks = [];
  for await (const chunk of stream) chunks.push(...chunk);
  return { bytes: Uint8Array.from(chunks), result: await future };
}

function error(code) {
  return { tag: "err", val: { tag: code } };
}

async function withDirectory(action) {
  const root = await mkdtemp(join(tmpdir(), "netwasm-wasi-fs-"));
  try {
    await mkdir(join(root, "granted", "sub"), { recursive: true });
    await writeFile(join(root, "granted", "a.txt"), "alpha");
    await writeFile(join(root, "granted", "sub", "b.txt"), "beta".repeat(20_000));
    await writeFile(join(root, "outside.txt"), "outside");
    await symlink(join(root, "outside.txt"), join(root, "granted", "escape"));
    await symlink(join(root, "granted", "a.txt"), join(root, "granted", "inside"));
    await action(join(root, "granted"));
  } finally {
    await rm(root, { recursive: true, force: true });
  }
}

test("reads granted host files through preopened directories", async () => {
  await withDirectory(async root => {
    const filesystem = createWasiFilesystem(Object.freeze({
      hostFiles,
      preopens: Object.freeze([Object.freeze({ access: "readOnly", guestPath: "/data", hostPath: root })]),
    }));
    const [[directory, path]] = filesystem.preopens.getDirectories();
    assert.equal(path, "/data");
    const { Descriptor } = filesystem.types;
    assert.throws(() => new Descriptor(), TypeError);
    assert.deepEqual(await directory.getType(), { tag: "ok", val: { tag: "directory" } });
    assert.deepEqual(await directory.getFlags(), { tag: "ok", val: { read: true } });

    const file = (await directory.openAt(noFlags, "./sub/../a.txt", noFlags, read)).val;
    assert.ok(file instanceof Descriptor);
    assert.deepEqual(await readAll(file.readViaStream(0n)), {
      bytes: new TextEncoder().encode("alpha"), result: { tag: "ok", val: undefined },
    });
    assert.deepEqual((await readAll(file.readViaStream(2n))).bytes, new TextEncoder().encode("pha"));
    const status = await file.stat();
    assert.equal(status.val.type.tag, "regular-file");
    assert.equal(status.val.size, 5n);
    assert.equal(typeof status.val.dataModificationTimestamp.seconds, "bigint");
    assert.equal((await directory.stat()).val.type.tag, "directory");

    const large = (await directory.openAt(noFlags, "sub/b.txt", noFlags, read)).val;
    assert.equal((await readAll(large.readViaStream(0n))).bytes.length, 80_000);
    assert.equal((await directory.openAt(noFlags, "sub", Object.freeze({ directory: true }), read)).tag, "ok");
    assert.deepEqual(await directory.openAt(noFlags, "a.txt", Object.freeze({ directory: true }), read),
      error("not-directory"));
    assert.deepEqual(await directory.openAt(noFlags, "missing", noFlags, read), error("no-entry"));
    assert.deepEqual(await directory.openAt(noFlags, "/etc/passwd", noFlags, read), error("not-permitted"));
    assert.deepEqual(await directory.openAt(noFlags, "../outside.txt", noFlags, read), error("not-permitted"));
    assert.deepEqual(await directory.openAt(noFlags, "escape", noFlags, read), error("loop"));
    assert.deepEqual(await directory.openAt(Object.freeze({ symlinkFollow: true }), "escape", noFlags, read),
      error("not-permitted"));
    assert.equal((await directory.openAt(Object.freeze({ symlinkFollow: true }), "inside", noFlags, read)).tag,
      "ok");
    assert.deepEqual(await directory.openAt(noFlags, "a.txt", Object.freeze({ create: true }), read),
      error("read-only"));
    assert.deepEqual(await file.openAt(noFlags, "x", noFlags, read), error("not-directory"));
    assert.deepEqual(await readAll(directory.readViaStream(0n)), {
      bytes: new Uint8Array(), result: error("is-directory"),
    });
    assert.equal(await file.isSameObject(file), true);
    assert.equal(await file.isSameObject(large), false);
    await filesystem.dispose();
    await filesystem.dispose();
  });
});

test("a read-write grant creates, writes, lists, renames and removes inside its directory", async () => {
  await withDirectory(async root => {
    const filesystem = createWasiFilesystem(Object.freeze({
      hostFiles,
      preopens: Object.freeze([Object.freeze({ access: "readWrite", guestPath: "/data", hostPath: root })]),
    }));
    const [[directory]] = filesystem.preopens.getDirectories();
    const write = Object.freeze({ read: true, write: true });
    const created = (await directory.openAt(noFlags, "new.txt", Object.freeze({ create: true }), write)).val;
    assert.deepEqual(await created.getFlags(), { tag: "ok", val: { read: true, write: true } });
    assert.deepEqual(await created.writeViaStream(stream([Uint8Array.of(104, 105)]), 0n), { tag: "ok", val: undefined });
    assert.deepEqual(await created.writeViaStream(stream([Uint8Array.of(33)]), 5n), { tag: "ok", val: undefined });
    assert.deepEqual(await created.appendViaStream(stream([Uint8Array.of(10)])), { tag: "ok", val: undefined });
    assert.deepEqual([...await readFile(join(root, "new.txt"))], [104, 105, 0, 0, 0, 33, 10]);
    assert.deepEqual(await created.setSize(2n), { tag: "ok", val: undefined });
    assert.equal(await readFile(join(root, "new.txt"), "utf8"), "hi");
    assert.deepEqual(await created.sync(), { tag: "ok", val: undefined });
    assert.deepEqual(await created.syncData(), { tag: "ok", val: undefined });
    assert.deepEqual(await created.advise(0n, 0n, "normal"), { tag: "ok", val: undefined });

    // create keeps an existing file; exclusive refuses it; truncate empties it.
    assert.equal((await directory.openAt(noFlags, "new.txt", Object.freeze({ create: true }), write)).tag, "ok");
    assert.equal(await readFile(join(root, "new.txt"), "utf8"), "hi");
    assert.deepEqual(await directory.openAt(noFlags, "new.txt", Object.freeze({ create: true, exclusive: true }), write),
      error("exist"));
    assert.equal((await directory.openAt(noFlags, "fresh", Object.freeze({ create: true, exclusive: true }), write)).tag, "ok");
    assert.equal((await directory.openAt(noFlags, "new.txt", Object.freeze({ truncate: true }), write)).tag, "ok");
    assert.equal(await readFile(join(root, "new.txt"), "utf8"), "");
    assert.equal((await directory.openAt(noFlags, ".", Object.freeze({ create: true, truncate: true }), read)).tag, "ok");
    assert.deepEqual(await directory.openAt(noFlags, "sub", noFlags, write), error("is-directory"));
    assert.deepEqual(await directory.openAt(noFlags, "x/../../y", Object.freeze({ create: true }), write),
      error("not-permitted"));
    assert.deepEqual(await directory.openAt(noFlags, "missing/x", Object.freeze({ create: true }), write),
      error("no-entry"));

    assert.deepEqual(await directory.createDirectoryAt("made"), { tag: "ok", val: undefined });
    assert.deepEqual(await directory.createDirectoryAt("made"), error("exist"));
    assert.deepEqual(await directory.renameAt("new.txt", directory, "made/moved.txt"), { tag: "ok", val: undefined });
    assert.deepEqual(await directory.symlinkAt("moved.txt", "made/link"), { tag: "ok", val: undefined });
    assert.deepEqual(await directory.readlinkAt("made/link"), { tag: "ok", val: "moved.txt" });
    assert.equal((await directory.statAt(noFlags, "made/link")).val.type.tag, "symbolic-link");
    assert.equal((await directory.statAt(Object.freeze({ symlinkFollow: true }), "made/link")).val.type.tag,
      "regular-file");
    assert.deepEqual(await directory.statAt(Object.freeze({ symlinkFollow: true }), "escape"), error("not-permitted"));
    assert.deepEqual(await directory.statAt(noFlags, "gone"), error("no-entry"));

    const [entries, listed] = (await directory.openAt(noFlags, "made", noFlags, read)).val.readDirectory();
    const names = [];
    for await (const entry of entries) names.push(`${entry.name}:${entry.type.tag}`);
    assert.deepEqual(names.sort(), ["link:symbolic-link", "moved.txt:regular-file"]);
    assert.deepEqual(await listed, { tag: "ok", val: undefined });

    const when = Object.freeze({ seconds: 1_600_000_000n, nanoseconds: 500_000_000 });
    assert.deepEqual(await directory.setTimesAt(noFlags, "made/moved.txt", Object.freeze({ tag: "timestamp", val: when }),
      Object.freeze({ tag: "no-change" })), { tag: "ok", val: undefined });
    assert.equal((await stat(join(root, "made", "moved.txt"))).atimeMs, 1_600_000_000_500);
    assert.deepEqual(await directory.setTimesAt(noFlags, "nowhere", Object.freeze({ tag: "now" }),
      Object.freeze({ tag: "now" })), error("no-entry"));
    assert.deepEqual(await created.setTimes(Object.freeze({ tag: "now" }), Object.freeze({ tag: "timestamp", val: when })),
      error("no-entry"));

    assert.deepEqual(await directory.unlinkFileAt("made"), error("is-directory"));
    assert.deepEqual(await directory.removeDirectoryAt("made"), error("not-empty"));
    assert.deepEqual(await directory.unlinkFileAt("made/link"), { tag: "ok", val: undefined });
    assert.deepEqual(await directory.unlinkFileAt("made/moved.txt"), { tag: "ok", val: undefined });
    assert.deepEqual(await directory.removeDirectoryAt("made"), { tag: "ok", val: undefined });
    assert.deepEqual(await directory.createDirectoryAt("."), error("invalid"));
    assert.deepEqual(await directory.createDirectoryAt("../out"), error("not-permitted"));
    assert.deepEqual(await directory.linkAt(), error("unsupported"));
    assert.deepEqual(await directory.metadataHash(), error("unsupported"));

    // A descriptor opened for reading neither writes nor resizes; a directory does neither.
    const reader = (await directory.openAt(noFlags, "a.txt", noFlags, read)).val;
    assert.deepEqual(await reader.writeViaStream(stream([]), 0n), error("bad-descriptor"));
    assert.deepEqual(await reader.setSize(0n), error("bad-descriptor"));
    assert.deepEqual(directory.setSize(0n), error("is-directory"));
    assert.deepEqual(await directory.appendViaStream(stream([])), error("is-directory"));
    assert.deepEqual(await reader.readDirectory()[1], error("not-directory"));
    assert.deepEqual(await reader.renameAt("x", directory, "y"), error("not-directory"));
    assert.deepEqual(await directory.renameAt("a.txt", reader, "y"), error("not-directory"));
    assert.deepEqual(await directory.renameAt("a.txt", {}, "y"), error("bad-descriptor"));
    assert.deepEqual(await directory.renameAt("a.txt", directory, "/abs"), error("not-permitted"));
    assert.deepEqual(await directory.renameAt("a.txt", directory, "."), error("not-permitted"));
    assert.deepEqual(await directory.symlinkAt(5, "x"), error("invalid"));
    assert.deepEqual(await directory.symlinkAt("a\0b", "x"), error("invalid"));

    await filesystem.dispose();
    for (const name of ["syncData", "getType", "getFlags", "stat", "openAt", "linkAt", "setTimes", "statAt",
      "createDirectoryAt", "setSize"]) {
      assert.deepEqual(await directory[name](noFlags, "a.txt", noFlags, read), error("bad-descriptor"), name);
    }
    assert.deepEqual(await directory.writeViaStream([], 0n), error("bad-descriptor"));
    assert.deepEqual(await directory.readDirectory()[1], error("bad-descriptor"));
    assert.deepEqual(await directory.readViaStream(0n)[1], error("bad-descriptor"));
    assert.equal(await directory.isSameObject(directory), false);
    assert.throws(() => filesystem.preopens.getDirectories(), /disposed/);
  });
});

test("a read-only grant and mounted files refuse every change", async () => {
  await withDirectory(async root => {
    const filesystem = createWasiFilesystem(Object.freeze({
      hostFiles,
      preopens: Object.freeze([Object.freeze({ access: "readOnly", guestPath: "/data", hostPath: root })]),
    }));
    filesystem.mountReadOnlyFile({ guestPath: "/zones/z", bytes: Uint8Array.of(1) });
    assert.throws(() => filesystem.mountReadOnlyFile({ guestPath: "/data/z", bytes: Uint8Array.of(1) }),
      /collides with an existing preopen/);
    const [[directory], [zones]] = filesystem.preopens.getDirectories();
    for (const target of [directory, zones]) {
      assert.deepEqual(await target.createDirectoryAt("x"), error("read-only"));
      assert.deepEqual(await target.setTimes(Object.freeze({ tag: "now" }), Object.freeze({ tag: "now" })), error("read-only"));
      assert.deepEqual(await directory.renameAt("a.txt", target, "y"), error("read-only"));
    }
    assert.deepEqual(await directory.openAt(noFlags, "a.txt", noFlags, Object.freeze({ mutateDirectory: true })),
      error("read-only"));
    assert.deepEqual(await zones.readlinkAt("z"), error("invalid"));
    assert.equal((await zones.statAt(noFlags, "z")).val.size, 1n);
    assert.deepEqual(await zones.statAt(noFlags, "y"), error("no-entry"));
    const [entries, listed] = zones.readDirectory();
    const names = [];
    for await (const entry of entries) names.push(entry.name);
    assert.deepEqual(names, ["z"]);
    assert.deepEqual(await listed, { tag: "ok", val: undefined });
    await filesystem.dispose();
  });
});

test("mounts read-only files until the guest first lists its directories", async () => {
  const filesystem = createWasiFilesystem(Object.freeze({ hostFiles: null, preopens: Object.freeze([]) }));
  const contents = Uint8Array.of(1, 2, 3, 4);
  const release = filesystem.mountReadOnlyFile({ guestPath: "/zones/data.bin", bytes: contents });
  contents[0] = 9;
  const removed = filesystem.mountReadOnlyFile({ guestPath: "/removed/x", bytes: new Uint8Array() });
  removed();
  // A program's sidecars share one mounted directory (issue 225).
  const other = filesystem.mountReadOnlyFile({ guestPath: "/zones/other", bytes: Uint8Array.of(7) });
  assert.throws(() => filesystem.mountReadOnlyFile({ guestPath: "/zones/data.bin", bytes: new Uint8Array() }),
    /already mounted/);
  for (const guestPath of [undefined, "relative", "/", "/a//b", "/a/./b", "/a/../b", "/a\\b"]) {
    assert.throws(() => filesystem.mountReadOnlyFile({ guestPath, bytes: new Uint8Array() }), TypeError);
  }
  assert.throws(() => filesystem.mountReadOnlyFile({ guestPath: "/c/d", bytes: [1] }), TypeError);
  other();
  const entries = filesystem.preopens.getDirectories();
  assert.deepEqual(entries.map(([, path]) => path), ["/zones"]);
  assert.deepEqual(await entries[0][0].openAt(noFlags, "other", noFlags, read), error("no-entry"));
  assert.throws(() => filesystem.mountReadOnlyFile({ guestPath: "/late/x", bytes: new Uint8Array() }),
    /setup is closed/);
  const [[directory]] = entries;
  assert.equal((await directory.openAt(noFlags, ".", noFlags, read)).tag, "ok");
  const file = (await directory.openAt(noFlags, "data.bin", noFlags, read)).val;
  assert.deepEqual(await readAll(file.readViaStream(1n)), {
    bytes: Uint8Array.of(2, 3, 4), result: { tag: "ok", val: undefined },
  });
  assert.deepEqual((await file.stat()).val, {
    type: { tag: "regular-file" }, linkCount: 1n, size: 4n,
    dataAccessTimestamp: undefined, dataModificationTimestamp: undefined, statusChangeTimestamp: undefined,
  });
  assert.equal((await directory.stat()).val.size, 0n);
  assert.deepEqual(await directory.openAt(noFlags, "missing", noFlags, read), error("no-entry"));
  assert.deepEqual(await directory.openAt(noFlags, "a/b", noFlags, read), error("no-entry"));
  assert.deepEqual(await directory.openAt(noFlags, "data.bin", noFlags, Object.freeze({ write: true })),
    error("read-only"));
  release();
  await filesystem.dispose();
  assert.throws(() => filesystem.mountReadOnlyFile({ guestPath: "/a/b", bytes: new Uint8Array() }),
    /setup is closed/);
});

test("maps host failures to error codes and closes open files at disposal", async () => {
  const closed = [];
  let reads = 0;
  const status = kind => ({
    isDirectory: () => kind === "directory",
    isFile: () => kind === "file",
    isSymbolicLink: () => false,
    nlink: 1, size: 1, atimeMs: 0, mtimeMs: 1000.5, ctimeMs: 2000,
  });
  const fake = fakeHost({
    lstatSync: path => status(path.endsWith("/dir") || path === "/root" ? "directory" : "file"),
    openSync: () => 7,
    readSync(file, buffer) {
      reads++;
      if (reads === 1) { buffer[0] = 5; return 1; }
      throw Object.assign(new Error("device"), { code: "EIO" });
    },
    closeSync() { closed.push("closed"); },
    realpathSync: path => path,
    statSync(path) {
      if (path.endsWith("/denied")) throw Object.assign(new Error("denied"), { code: "EACCES" });
      if (path.endsWith("/socket")) return status("socket");
      return status(path === "/root" || path.endsWith("/dir") ? "directory" : "file");
    },
  });
  const filesystem = createWasiFilesystem(Object.freeze({
    hostFiles: fake,
    preopens: Object.freeze([Object.freeze({ access: "readOnly", guestPath: "/r", hostPath: "/root" })]),
  }));
  const [[directory]] = filesystem.preopens.getDirectories();
  assert.deepEqual(directory.openAt(noFlags, "denied", noFlags, read), error("access"));
  assert.deepEqual(directory.openAt(noFlags, "socket", noFlags, read), error("unsupported"));
  const file = directory.openAt(noFlags, "file", noFlags, read).val;
  assert.deepEqual(file.stat().val.dataModificationTimestamp, { seconds: 1n, nanoseconds: 500_000 });
  const denied = createWasiFilesystem(Object.freeze({
    hostFiles: { ...fake, statSync: () => { throw new Error("unknown"); } },
    preopens: Object.freeze([Object.freeze({ access: "readOnly", guestPath: "/r", hostPath: "C:\\root" })]),
  }));
  assert.deepEqual(denied.preopens.getDirectories()[0][0].stat(), error("io"));
  assert.deepEqual(await readAll(file.readViaStream(0n)), { bytes: Uint8Array.of(5), result: error("io") });
  assert.deepEqual(closed, ["closed"]);

  // Disposal closes a file a stream still reads; ending the stream then leaves it closed.
  reads = 0;
  const [stream, future] = file.readViaStream(0n);
  const iterator = stream[Symbol.asyncIterator]();
  assert.deepEqual([...(await iterator.next()).value], [5]);
  await filesystem.dispose();
  assert.deepEqual(closed, ["closed", "closed"]);
  await iterator.return();
  assert.deepEqual(closed, ["closed", "closed"]);
  assert.deepEqual(await future, { tag: "ok", val: undefined });
});

test("treats foreign receivers as bad descriptors and contains paths under a root ending in a slash", async () => {
  const closes = [];
  const file = { isDirectory: () => false, isFile: () => true, isSymbolicLink: () => false };
  let opened = 0;
  const filesystem = createWasiFilesystem(Object.freeze({
    hostFiles: fakeHost({
      lstatSync: () => file,
      openSync: () => ++opened,
      readSync: (handle, buffer) => (handle === 1 ? 0 : (buffer[0] = 1, 1)),
      closeSync(handle) { closes.push(handle); throw new Error("close failed"); },
      realpathSync: path => (path === "/" ? "/" : path),
      statSync: () => file,
    }),
    preopens: Object.freeze([Object.freeze({ access: "readOnly", guestPath: "/", hostPath: "/" })]),
  }));
  const [[root]] = filesystem.preopens.getDirectories();
  const { Descriptor } = filesystem.types;
  assert.deepEqual(Descriptor.prototype.stat.call({}), error("bad-descriptor"));
  const opened1 = root.openAt(noFlags, "etc/hosts", noFlags, read).val;
  assert.equal(opened1.isSameObject({}), false);
  assert.deepEqual(await readAll(opened1.readViaStream(0n)), { bytes: new Uint8Array(), result: { tag: "ok", val: undefined } });
  const [stream] = opened1.readViaStream(0n);
  stream[Symbol.iterator]().next();
  await filesystem.dispose();
  assert.deepEqual(closes, [1, 2]);

  const top = createWasiFilesystem(Object.freeze({ hostFiles: null, preopens: Object.freeze([]) }));
  top.mountReadOnlyFile({ guestPath: "/file", bytes: Uint8Array.of(1) });
  assert.deepEqual(top.preopens.getDirectories().map(([, path]) => path), ["/"]);
});

test("rejects malformed requests, host file systems and grants", () => {
  const grant = Object.freeze({ access: "readOnly", guestPath: "/a", hostPath: "/host" });
  for (const request of [
    null, {}, { hostFiles: null, preopens: Object.freeze([]), extra: 1 },
    { hostFiles: {}, preopens: Object.freeze([]) },
    { hostFiles: { ...hostFiles, openSync: null }, preopens: Object.freeze([]) },
    { hostFiles: null, preopens: [] },
    { hostFiles: null, preopens: Object.freeze([grant]) },
    { hostFiles, preopens: Object.freeze([{ ...grant }]) },
    { hostFiles, preopens: Object.freeze([Object.freeze({ ...grant, hostPath: "relative" })]) },
    { hostFiles, preopens: Object.freeze([Object.freeze({ ...grant, guestPath: "relative" })]) },
    { hostFiles, preopens: Object.freeze([Object.freeze({ ...grant, access: "all" })]) },
    { hostFiles, preopens: Object.freeze([grant, grant]) },
    { hostFiles, preopens: Object.freeze([Object.freeze({ access: "readOnly", guestPath: "/a" })]) },
  ]) {
    assert.throws(() => createWasiFilesystem(request), TypeError);
  }
});

test("maps failing host changes to error codes and stops reading a stream it cannot write", async () => {
  const status = kind => ({
    isDirectory: () => kind === "directory", isFile: () => kind === "file", isSymbolicLink: () => kind === "link",
    nlink: 1, size: 0, atimeMs: 1000, mtimeMs: 2000, ctimeMs: 0,
  });
  const writes = [];
  const dropped = [];
  const times = [];
  const filesystem = createWasiFilesystem(Object.freeze({
    hostFiles: fakeHost({
      lstatSync: path => status(path === "/root" ? "directory" : "file"),
      realpathSync: path => (path.startsWith("/root/out") ? "/elsewhere" : path),
      statSync: path => status(path === "/root" ? "directory" : "file"),
      openSync: () => 3,
      writeSync(file, chunk, offset, length) {
        writes.push(length);
        throw Object.assign(new Error("full"), { code: "ENOSPC" });
      },
      ftruncateSync() { throw Object.assign(new Error("big"), { code: "EFBIG" }); },
      closeSync() {},
      readdirSync: () => [
        { name: "fifo", isDirectory: () => false, isFile: () => false, isSymbolicLink: () => false },
        { name: "d", isDirectory: () => true, isFile: () => false, isSymbolicLink: () => false },
      ],
      utimesSync(path, access, modification) { times.push([access, modification]); },
      mkdirSync() { throw Object.assign(new Error("ro"), { code: "EROFS" }); },
    }),
    preopens: Object.freeze([Object.freeze({ access: "readWrite", guestPath: "/r", hostPath: "/root" })]),
  }));
  const [[directory]] = filesystem.preopens.getDirectories();
  const file = directory.openAt(noFlags, "f", noFlags, Object.freeze({ read: true, write: true })).val;
  for (const guestStream of [
    { ...stream([Uint8Array.of(1)]), [Symbol.dispose]() { dropped.push("dispose"); } },
    { ...stream([Uint8Array.of(1)]), [Symbol.for("dispose")]() { dropped.push("registered"); } },
    { ...stream([Uint8Array.of(1)]), return() { dropped.push("return"); } },
    { ...stream([Uint8Array.of(1)]), [Symbol.dispose]() { throw new Error("gone"); } },
    stream([Uint8Array.of(1)]),
    drained([[1]], () => dropped.push("drained")),
  ]) {
    assert.deepEqual(await file.writeViaStream(guestStream, 0n), error("insufficient-space"));
  }
  assert.deepEqual(dropped, ["dispose", "registered", "return", "drained"]);
  assert.deepEqual(writes, [1, 1, 1, 1, 1, 1]);
  assert.deepEqual(file.setSize(1n), error("file-too-large"));
  assert.deepEqual(directory.openAt(noFlags, "f", Object.freeze({ truncate: true }), read), error("file-too-large"));
  assert.deepEqual(directory.openAt(noFlags, "out/x", Object.freeze({ create: true }), read), error("not-permitted"));
  assert.deepEqual(directory.createDirectoryAt("x"), error("read-only"));

  const [entries, listed] = directory.readDirectory();
  const types = [];
  for await (const entry of entries) types.push(entry.type);
  assert.deepEqual(types, [{ tag: "other", val: undefined }, { tag: "directory" }]);
  assert.deepEqual(await listed, { tag: "ok", val: undefined });

  assert.deepEqual(file.setTimes(Object.freeze({ tag: "no-change" }),
    Object.freeze({ tag: "timestamp", val: Object.freeze({ seconds: 3n, nanoseconds: 0 }) })), { tag: "ok", val: undefined });
  assert.deepEqual(file.setTimes(Object.freeze({ tag: "now" }), Object.freeze({ tag: "now" })), { tag: "ok", val: undefined });
  assert.deepEqual(times[0], [1, 3]);
  assert.equal(times[1].every(value => value > 1_000_000), true);

  const failing = createWasiFilesystem(Object.freeze({
    hostFiles: fakeHost({
      statSync: () => status("directory"),
      readdirSync: () => { throw Object.assign(new Error("denied"), { code: "EACCES" }); },
      openSync: () => { throw Object.assign(new Error("denied"), { code: "EACCES" }); },
    }),
    preopens: Object.freeze([Object.freeze({ access: "readOnly", guestPath: "/r", hostPath: "/root/" })]),
  }));
  const [listing, result] = failing.preopens.getDirectories()[0][0].readDirectory();
  for await (const entry of listing) assert.fail(`unexpected ${entry.name}`);
  assert.deepEqual(await result, error("access"));
  await filesystem.dispose();
});

// A raw host's guest stream: what the guest has written so far, then null once it dropped the
// stream; undefined while it may still write. The rest arrives through read().
function drained(chunks, onDrop = () => {}, later = []) {
  return {
    [syncDrainKey]: () => (chunks.length === 0 ? (later.length === 0 ? null : undefined) : chunks.shift()),
    read: async () => (later.length === 0 ? { done: true } : { done: false, value: later.shift() }),
    [Symbol.dispose]: onDrop,
  };
}

// Raw hosts read a sync provider's results at once, so the guest never waits.
test("a raw host's buffered writes and reads complete synchronously", async () => {
  await withDirectory(async root => {
    const filesystem = createWasiFilesystem(Object.freeze({
      hostFiles,
      preopens: Object.freeze([Object.freeze({ access: "readWrite", guestPath: "/data", hostPath: root })]),
    }));
    const [[directory]] = filesystem.preopens.getDirectories();
    const write = Object.freeze({ read: true, write: true });
    const file = directory.openAt(noFlags, "raw.txt", Object.freeze({ create: true }), write).val;
    const written = readSyncFuture(file.writeViaStream(drained([[104], Uint8Array.of(105)]), 0n));
    assert.deepEqual(written(), { tag: "ok", val: undefined });
    assert.equal(await readFile(join(root, "raw.txt"), "utf8"), "hi");

    // A guest still writing: the rest is copied as it arrives.
    const pending = readSyncFuture(file.appendViaStream(drained([[33]], undefined, [Uint8Array.of(63)])))();
    assert.equal(typeof pending.then, "function");
    assert.deepEqual(await pending, { tag: "ok", val: undefined });
    assert.equal(await readFile(join(root, "raw.txt"), "utf8"), "hi!?");

    const [stream, future] = file.readViaStream(1n);
    const bytes = [];
    for (const chunk of stream) bytes.push(...chunk);
    assert.deepEqual(bytes, [105, 33, 63]);
    assert.deepEqual(readSyncFuture(future)(), { tag: "ok", val: undefined });
    assert.deepEqual(readSyncFuture(directory.readViaStream(0n)[1])(), error("is-directory"));

    // The file goes away after it was opened for writing.
    let opens = 0;
    const missing = createWasiFilesystem(Object.freeze({
      hostFiles: {
        ...hostFiles,
        openSync: (path, flags) => {
          if (++opens === 1) return openSync(path, flags);
          throw Object.assign(new Error("gone"), { code: "ENOENT" });
        },
      },
      preopens: Object.freeze([Object.freeze({ access: "readWrite", guestPath: "/data", hostPath: root })]),
    }));
    const gone = missing.preopens.getDirectories()[0][0].openAt(noFlags, "raw.txt", noFlags, write).val;
    assert.deepEqual(readSyncFuture(gone.writeViaStream(drained([[1]]), 0n))(), error("no-entry"));
    assert.deepEqual(await gone.writeViaStream(stream2([Uint8Array.of(1)]), 0n), error("no-entry"));
    await filesystem.dispose();
  });
});

function stream2(chunks) {
  return { read: async () => (chunks.length === 0 ? { done: true } : { done: false, value: chunks.shift() }) };
}
