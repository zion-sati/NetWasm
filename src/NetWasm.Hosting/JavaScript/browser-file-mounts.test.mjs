import assert from "node:assert/strict";
import test from "node:test";

import { createBrowserFileMounts } from "./browser-file-mounts.mjs";
import { readSyncFuture } from "./sync-values.mjs";
import { createWasiFilesystem } from "./wasi-filesystem.mjs";

const noFlags = Object.freeze({});
const read = Object.freeze({ read: true });
const write = Object.freeze({ read: true, write: true });
const create = Object.freeze({ create: true });
const encoder = new TextEncoder();
const decoder = new TextDecoder();

// What a worker's FileReaderSync would read: the test knows every blob's bytes.
const blobBytes = new WeakMap();
function blob(text, name = null) {
  const bytes = encoder.encode(text);
  const value = name === null ? new Blob([bytes]) : new File([bytes], name, { lastModified: 1_000_000 });
  blobBytes.set(value, bytes);
  return value;
}
const readBlobSync = (value, start, end) => blobBytes.get(value).subarray(start, end);

// A FileSystemFileHandle and FileSystemDirectoryHandle as the File System Access API has them.
function fileHandle(text, { writable = true } = {}) {
  const handle = {
    kind: "file",
    bytes: encoder.encode(text),
    writes: 0,
    async getFile() { return new File([handle.bytes], "picked", { lastModified: 2_000_000 }); },
  };
  if (writable) {
    handle.createWritable = async () => ({
      async write(data) { handle.bytes = new Uint8Array(data); handle.writes++; },
      async close() {},
    });
  }
  return handle;
}

function directoryHandle(entries = {}) {
  const children = new Map(Object.entries(entries));
  return {
    kind: "directory",
    children,
    async *entries() { yield* children; },
    async getDirectoryHandle(name, { create: make }) {
      if (!children.has(name) && make) children.set(name, directoryHandle());
      return children.get(name);
    },
    async getFileHandle(name, { create: make }) {
      if (!children.has(name) && make) children.set(name, fileHandle(""));
      return children.get(name);
    },
    async removeEntry(name) { children.delete(name); },
  };
}

function view(mounts) {
  const filesystem = createWasiFilesystem(Object.freeze({
    hostFiles: mounts.hostFiles,
    preopens: Object.freeze([Object.freeze({ access: "readWrite", guestPath: "/", hostPath: "/" })]),
  }));
  return filesystem.preopens.getDirectories()[0][0];
}

function writeText(root, path, text, flags = create) {
  const file = root.openAt(noFlags, path, flags, write).val;
  const bytes = encoder.encode(text);
  return file.writeViaStream({ read: async () => (bytes.length === 0 ? { done: true } : { done: false, value: bytes.splice?.(0) ?? takeAll(bytes) }) }, 0n);
}

function takeAll(bytes) {
  const copy = bytes.slice();
  bytes.fill(0);
  return copy;
}

async function readText(root, path) {
  const file = root.openAt(noFlags, path, noFlags, read);
  if (file.tag === "err") return file.val.tag;
  const [stream, future] = file.val.readViaStream(0n);
  const bytes = [];
  for (const chunk of stream) bytes.push(...chunk);
  assert.deepEqual(readSyncFuture(future)(), { tag: "ok", val: undefined });
  return decoder.decode(Uint8Array.from(bytes));
}

async function put(root, path, text) {
  const file = root.openAt(noFlags, path, create, write);
  assert.equal(file.tag, "ok", `${path}: ${file.val?.tag}`);
  const bytes = encoder.encode(text);
  let sent = false;
  const result = await file.val.writeViaStream({
    read: async () => (sent ? { done: true } : (sent = true, { done: false, value: bytes })),
  }, 0n);
  assert.deepEqual(result, { tag: "ok", val: undefined });
}

test("the memory tree is an ordinary read-write file system for the guest", async () => {
  const mounts = createBrowserFileMounts({ readBlobSync: null });
  const root = view(mounts);
  assert.equal(root.statAt(noFlags, "tmp").val.type.tag, "directory");
  await put(root, "tmp/a.txt", "alpha");
  assert.equal(await readText(root, "tmp/a.txt"), "alpha");
  const file = root.openAt(noFlags, "tmp/a.txt", noFlags, write).val;
  assert.deepEqual(file.setSize(8n), { tag: "ok", val: undefined });
  assert.equal(file.stat().val.size, 8n);
  assert.deepEqual(file.setSize(2n), { tag: "ok", val: undefined });
  assert.equal(await readText(root, "tmp/a.txt"), "al");
  let appended = false;
  assert.deepEqual(await file.appendViaStream({
    read: async () => (appended ? { done: true } : (appended = true, { done: false, value: encoder.encode("!") })),
  }), { tag: "ok", val: undefined });
  assert.equal(await readText(root, "tmp/a.txt"), "al!");

  assert.deepEqual(root.renameAt("tmp/a.txt", root, "tmp/b.txt"), { tag: "ok", val: undefined });
  assert.equal(await readText(root, "tmp/a.txt"), "no-entry");
  assert.deepEqual(root.symlinkAt("b.txt", "tmp/link"), { tag: "ok", val: undefined });
  assert.deepEqual(root.symlinkAt("/tmp", "abs"), { tag: "ok", val: undefined });
  assert.deepEqual(root.readlinkAt("tmp/link"), { tag: "ok", val: "b.txt" });
  assert.equal(root.statAt(noFlags, "tmp/link").val.type.tag, "symbolic-link");
  assert.equal(root.statAt(noFlags, "tmp/link").val.size, 5n);
  assert.equal(root.statAt(Object.freeze({ symlinkFollow: true }), "tmp/link").val.type.tag, "regular-file");
  assert.equal(await readText(root, "abs/b.txt"), "al!");
  assert.deepEqual(root.readlinkAt("tmp/b.txt"), { tag: "err", val: { tag: "invalid" } });

  const [entries] = root.openAt(noFlags, "tmp", noFlags, read).val.readDirectory();
  assert.deepEqual([...entries].map(entry => `${entry.name}:${entry.type.tag}`).sort(),
    ["b.txt:regular-file", "link:symbolic-link"]);
  const when = Object.freeze({ seconds: 1_600_000_000n, nanoseconds: 0 });
  assert.deepEqual(root.setTimesAt(noFlags, "tmp/b.txt", Object.freeze({ tag: "timestamp", val: when }),
    Object.freeze({ tag: "timestamp", val: when })), { tag: "ok", val: undefined });
  assert.equal(root.statAt(noFlags, "tmp/b.txt").val.dataModificationTimestamp.seconds, 1_600_000_000n);

  const error = code => ({ tag: "err", val: { tag: code } });
  assert.deepEqual(root.createDirectoryAt("tmp"), error("exist"));
  assert.deepEqual(root.removeDirectoryAt("tmp"), error("not-empty"));
  assert.deepEqual(root.removeDirectoryAt("tmp/b.txt"), error("not-directory"));
  assert.deepEqual(root.unlinkFileAt("tmp"), error("is-directory"));
  assert.deepEqual(root.openAt(noFlags, "tmp/b.txt/x", noFlags, read), error("not-directory"));
  assert.deepEqual(root.openAt(noFlags, "nowhere/x", create, write), error("no-entry"));
  assert.deepEqual(root.openAt(noFlags, "tmp", noFlags, write), error("is-directory"));
  assert.deepEqual(root.openAt(noFlags, "tmp/b.txt", Object.freeze({ create: true, exclusive: true }), write), error("exist"));
  assert.deepEqual(root.symlinkAt("x", "tmp/link"), error("exist"));
  assert.deepEqual(root.createDirectoryAt("tmp/sub"), { tag: "ok", val: undefined });
  assert.deepEqual(root.renameAt("tmp", root, "tmp/sub/inside"), error("invalid"));
  assert.deepEqual(root.renameAt("tmp/b.txt", root, "tmp/sub"), error("is-directory"));
  assert.deepEqual(root.createDirectoryAt("full"), { tag: "ok", val: undefined });
  await put(root, "full/x", "x");
  assert.deepEqual(root.renameAt("tmp/sub", root, "full"), error("not-empty"));
  assert.deepEqual(root.renameAt("tmp/sub", root, "moved"), { tag: "ok", val: undefined });
  assert.deepEqual(root.removeDirectoryAt("moved"), { tag: "ok", val: undefined });
  assert.deepEqual(root.unlinkFileAt("tmp/link"), { tag: "ok", val: undefined });
  for (let index = 0; index < 34; index++) root.symlinkAt(`loop${(index + 1) % 34}`, `loop${index}`);
  assert.deepEqual(root.statAt(Object.freeze({ symlinkFollow: true }), "loop0"), error("loop"));
});

test("a picked File mounts read-only: read in place in a worker, copied in on the page", async () => {
  for (const readBlob of [readBlobSync, null]) {
    const mounts = createBrowserFileMounts({ readBlobSync: readBlob });
    const root = view(mounts);
    const picked = blob("picked contents", "photo.jpg");
    await mounts.mount("/input/photo.jpg", picked);
    await mounts.mount("/input/raw", blob("raw"));
    assert.equal(await readText(root, "input/photo.jpg"), "picked contents");
    assert.equal(root.statAt(noFlags, "input/photo.jpg").val.size, 15n);
    assert.equal(root.statAt(noFlags, "input/photo.jpg").val.dataModificationTimestamp.seconds, 1000n);
    assert.equal(await readText(root, "input/raw"), "raw");
    const error = code => ({ tag: "err", val: { tag: code } });
    assert.deepEqual(root.openAt(noFlags, "input/photo.jpg", noFlags, write), error("read-only"));
    assert.deepEqual(root.unlinkFileAt("input/photo.jpg"), error("busy"));
    assert.deepEqual(root.renameAt("input/photo.jpg", root, "x"), error("busy"));
    assert.deepEqual(root.renameAt("input/raw", root, "input/photo.jpg"), error("busy"));
    assert.deepEqual(root.setTimesAt(noFlags, "input/raw", Object.freeze({ tag: "now" }), Object.freeze({ tag: "now" })),
      error("read-only"));
    await assert.rejects(() => mounts.sync("/input/raw"), /read-only/);
    assert.deepEqual(mounts.list().map(entry => entry.guestPath), ["/input/photo.jpg", "/input/raw"]);
    assert.equal(mounts.list()[0].source, picked);
    mounts.unmount("/input/raw");
    assert.equal(await readText(root, "input/raw"), "no-entry");
    assert.throws(() => mounts.unmount("/input/raw"), /not mounted/);
  }
});

test("a picked handle is copied in and written back by sync", async () => {
  const mounts = createBrowserFileMounts({ readBlobSync: null });
  const root = view(mounts);
  const single = fileHandle("draft");
  await mounts.mount("/docs/draft.txt", single);
  await put(root, "docs/draft.txt", "final");
  assert.equal(single.writes, 0);
  await mounts.sync("/docs/draft.txt");
  assert.equal(decoder.decode(single.bytes), "final");
  await mounts.sync("/docs/draft.txt");
  assert.equal(single.writes, 1);

  const kept = fileHandle("kept");
  const gone = fileHandle("gone");
  const nested = directoryHandle({ "deep.txt": fileHandle("deep") });
  const folder = directoryHandle({ "kept.txt": kept, "gone.txt": gone, nested });
  await mounts.mount("/work", folder);
  assert.equal(await readText(root, "work/nested/deep.txt"), "deep");
  assert.deepEqual(root.unlinkFileAt("work/gone.txt"), { tag: "ok", val: undefined });
  await put(root, "work/new.txt", "new");
  root.openAt(noFlags, "work/empty", create, write);
  assert.deepEqual(root.createDirectoryAt("work/made"), { tag: "ok", val: undefined });
  await put(root, "work/made/inner.txt", "inner");
  assert.deepEqual(root.symlinkAt("kept.txt", "work/link"), { tag: "ok", val: undefined });
  assert.deepEqual(root.unlinkFileAt("work"), { tag: "err", val: { tag: "is-directory" } });
  assert.deepEqual(root.removeDirectoryAt("work/made"), { tag: "err", val: { tag: "not-empty" } });
  await mounts.sync("/work");
  assert.deepEqual([...folder.children.keys()].sort(), ["empty", "kept.txt", "made", "nested", "new.txt"]);
  assert.equal(decoder.decode(folder.children.get("new.txt").bytes), "new");
  assert.equal(folder.children.get("empty").writes, 1);
  assert.equal(decoder.decode(folder.children.get("made").children.get("inner.txt").bytes), "inner");
  assert.equal(kept.writes, 0);

  // A link replacing a picked entry is removed from the handle on sync.
  folder.children.set("link", fileHandle("stale"));
  await mounts.sync("/work");
  assert.equal(folder.children.has("link"), false);

  const readOnlyHandle = fileHandle("x", { writable: false });
  await mounts.mount("/locked", readOnlyHandle);
  await put(root, "locked", "changed");
  await assert.rejects(() => mounts.sync("/locked"), /createWritable/);
});

test("mounts and the host file system reject what they cannot take", async () => {
  assert.throws(() => createBrowserFileMounts(null), /options is invalid/);
  assert.throws(() => createBrowserFileMounts({}), /shape is invalid/);
  assert.throws(() => createBrowserFileMounts({ readBlobSync: 1 }), /function or null/);
  const mounts = createBrowserFileMounts({ readBlobSync: null });
  const root = view(mounts);
  await mounts.mount("/a/file", blob("x"));
  await assert.rejects(() => mounts.mount("/a/file", blob("y")), /already mounted/);
  await assert.rejects(() => mounts.mount("/a/file/below", blob("y")), /not below a directory/);
  await put(root, "taken", "t");
  await assert.rejects(() => mounts.mount("/taken", blob("y")), /already in use/);
  await assert.rejects(() => mounts.mount("/", blob("y")), /below \//);
  for (const path of ["relative", "/a/../b", "/a/./b", "/a\\b", "/a\0b", 1]) {
    await assert.rejects(() => mounts.mount(path, blob("y")), /canonical absolute path/);
  }
  for (const source of [null, {}, "text", { kind: "file" }, { kind: "directory" }]) {
    await assert.rejects(() => mounts.mount("/b", source), /must mount a File, Blob/);
  }
  await assert.rejects(() => mounts.sync("/nothing"), /not mounted/);
  await mounts.mount("/dir", directoryHandle());
  assert.deepEqual(root.removeDirectoryAt("dir"), { tag: "err", val: { tag: "busy" } });

  const files = mounts.hostFiles;
  assert.throws(() => files.closeSync(99), error => error.code === "EBADF");
  assert.throws(() => files.readSync(99, new Uint8Array(1), 0, 1, 0), error => error.code === "EBADF");
  assert.throws(() => files.openSync("/missing", "r"), error => error.code === "ENOENT");
  assert.throws(() => files.openSync("/missing/x", "a"), error => error.code === "ENOENT");
  assert.throws(() => files.readdirSync("/taken"), error => error.code === "ENOTDIR");
  const empty = createBrowserFileMounts({ readBlobSync: null }).hostFiles;
  empty.rmdirSync("/tmp");
  assert.throws(() => empty.rmdirSync("/"), error => error.code === "EBUSY");
  assert.throws(() => files.mkdirSync("/"), error => error.code === "EEXIST");
  assert.throws(() => files.openSync("/dir", "r"), error => error.code === "EISDIR");
  assert.throws(() => files.unlinkSync("/dir"), error => error.code === "EISDIR");
  files.symlinkSync("./taken", "/dot");
  assert.equal(files.realpathSync("/dot"), "/taken");
  files.mkdirSync("/sub");
  files.symlinkSync("../taken", "/sub/up");
  assert.equal(files.realpathSync("/sub/up"), "/taken");
  const handle = files.openSync("/taken", "r");
  const buffer = new Uint8Array(4);
  assert.equal(files.readSync(handle, buffer, 1, 4, 10), 0);
  files.closeSync(handle);
});
