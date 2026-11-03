import assert from "node:assert/strict";
import { mkdir, mkdtemp, rm, writeFile } from "node:fs/promises";
import { tmpdir } from "node:os";
import path from "node:path";
import { spawnSync } from "node:child_process";
import test from "node:test";
import { fileURLToPath } from "node:url";

import { restoreHostPaths, withCapturedStandardError, withDirectoryListing } from "./run-wasm-tools.mjs";

const runner = fileURLToPath(new URL("./run-wasm-tools.mjs", import.meta.url));

test("propagates a WASI program exit code", async () => {
  const directory = await mkdtemp(path.join(tmpdir(), "netwasm-wasi-exit-"));
  try {
    const modulePath = path.join(directory, "exit.wasm");
    await writeFile(modulePath, createExitModule(7));

    const result = spawnSync(process.execPath, [runner, modulePath], {
      encoding: "utf8",
    });

    assert.equal(result.status, 7);
    assert.equal(result.signal, null);
  } finally {
    await rm(directory, { recursive: true, force: true });
  }
});

test("names the host directories that arguments were mounted from", () => {
  const mounts = { ".": "/work", "/netwasm/paths/0": "/srv/project", "/netwasm/paths/10": "/opt/wit" };
  assert.equal(
    restoreHostPaths("  --> /netwasm/paths/0/world.wit:3:12 and /netwasm/paths/10/deps/a.wit", mounts),
    "  --> /srv/project/world.wit:3:12 and /opt/wit/deps/a.wit");
  // An unmounted root and other text stay as they are.
  assert.equal(restoreHostPaths("/netwasm/paths/7/x.wit /netwasm/pathsX", mounts), "/netwasm/paths/7/x.wit /netwasm/pathsX");
});

test("captures standard error and passes other writes through", () => {
  const memory = new WebAssembly.Memory({ initial: 1 });
  const bytes = new Uint8Array(memory.buffer);
  const view = new DataView(memory.buffer);
  bytes.set(new TextEncoder().encode("error: oops"), 64);
  view.setUint32(16, 64, true);
  view.setUint32(20, 11, true);
  const passed = [];
  const imports = { wasi_snapshot_preview1: { fd_write: (fd) => { passed.push(fd); return 0; } } };
  const chunks = [];
  const wrapped = withCapturedStandardError(imports, () => memory, chunks);

  assert.equal(wrapped.wasi_snapshot_preview1.fd_write(2, 16, 1, 32), 0);
  assert.equal(view.getUint32(32, true), 11);
  assert.equal(Buffer.concat(chunks).toString("utf8"), "error: oops");
  assert.equal(wrapped.wasi_snapshot_preview1.fd_write(1, 16, 1, 32), 0);
  assert.deepEqual(passed, [1]);
});

function createExitModule(exitCode) {
  const moduleName = Buffer.from("wasi_snapshot_preview1");
  const importName = Buffer.from("proc_exit");
  const exportName = Buffer.from("_start");
  const memoryName = Buffer.from("memory");
  return Buffer.from([
    0x00, 0x61, 0x73, 0x6d, 0x01, 0x00, 0x00, 0x00,
    0x01, 0x08, 0x02, 0x60, 0x01, 0x7f, 0x00, 0x60, 0x00, 0x00,
    0x02, 0x24, 0x01,
    moduleName.length, ...moduleName,
    importName.length, ...importName,
    0x00, 0x00,
    0x03, 0x02, 0x01, 0x01,
    0x05, 0x03, 0x01, 0x00, 0x01,
    0x07, 0x13, 0x02,
    exportName.length, ...exportName, 0x00, 0x01,
    memoryName.length, ...memoryName, 0x02, 0x00,
    0x0a, 0x08, 0x01, 0x06, 0x00, 0x41, exitCode, 0x10, 0x00, 0x0b,
  ]);
}

function listing(directories, original = {}) {
  const memory = new WebAssembly.Memory({ initial: 1 });
  const calls = [];
  const preview1 = {
    path_open(...values) {
      calls.push(["path_open", ...values]);
      new DataView(memory.buffer).setUint32(values[8], 9, true);
      return original.openResult ?? 0;
    },
    fd_close(fd) { calls.push(["fd_close", fd]); return 0; },
    // Node's WASI on Windows: uvwasi has no directory listing there.
    fd_readdir() { calls.push(["fd_readdir"]); return 52; },
    clock_time_get() { return 0; },
  };
  const imports = withDirectoryListing({ wasi_snapshot_preview1: preview1, other: {} }, directories, () => memory);
  return { calls, imports: imports.wasi_snapshot_preview1, memory, other: imports.other };
}

function readEntries(memory, used) {
  const view = new DataView(memory.buffer);
  const entries = [];
  for (let offset = 0; offset < used;) {
    const next = view.getBigUint64(offset, true);
    const length = view.getUint32(offset + 16, true);
    const type = view.getUint8(offset + 20);
    const name = new TextDecoder().decode(new Uint8Array(memory.buffer, offset + 24, length));
    entries.push([next, name, type]);
    offset += 24 + length;
  }
  return entries;
}

test("lists preopened and opened directories in sorted preview1 dirents", async () => {
  const root = await mkdtemp(path.join(tmpdir(), "netwasm-wasi-readdir-"));
  try {
    await mkdir(path.join(root, "deps"));
    await writeFile(path.join(root, "world.wit"), "package a:b;");
    await writeFile(path.join(root, "deps", "z.wit"), "");
    const { calls, imports, memory, other } = listing(["/unused", root]);
    const view = new DataView(memory.buffer);

    assert.equal(imports.fd_readdir(4, 0, 4096, 0n, 8192), 0);
    const used = view.getUint32(8192, true);
    assert.deepEqual(readEntries(memory, used), [[1n, "deps", 3], [2n, "world.wit", 4]]);
    // A cookie resumes after the entries already read.
    assert.equal(imports.fd_readdir(4, 0, 4096, 1n, 8192), 0);
    assert.deepEqual(readEntries(memory, view.getUint32(8192, true)), [[2n, "world.wit", 4]]);
    assert.equal(imports.fd_readdir(4, 0, 4096, 2n, 8192), 0);
    assert.equal(view.getUint32(8192, true), 0);

    // A directory opened through a preopen can be listed too, until it is closed.
    new Uint8Array(memory.buffer).set(new TextEncoder().encode("deps"), 1000);
    assert.equal(imports.path_open(4, 0, 1000, 4, 0, 0n, 0n, 0, 2000), 0);
    assert.equal(imports.fd_readdir(9, 0, 4096, 0n, 8192), 0);
    assert.deepEqual(readEntries(memory, view.getUint32(8192, true)), [[1n, "z.wit", 4]]);
    assert.equal(imports.fd_close(9), 0);
    assert.equal(imports.fd_readdir(9, 0, 4096, 0n, 8192), 8);
    // The platform's fd_readdir is never consulted.
    assert.deepEqual(calls.map(call => call[0]), ["path_open", "fd_close"]);
    assert.equal(imports.clock_time_get(), 0);
    assert.deepEqual(other, {});

    // A record that does not fit fills the buffer, so the caller retries larger.
    assert.equal(imports.fd_readdir(4, 0, 30, 0n, 8192), 0);
    assert.equal(view.getUint32(8192, true), 30);
  } finally {
    await rm(root, { recursive: true, force: true });
  }
});

test("reports unknown descriptors, failed opens and unreadable directories", async () => {
  const { imports, memory } = listing([path.join(tmpdir(), "netwasm-missing-directory-for-test")], { openResult: 44 });
  assert.equal(imports.fd_readdir(7, 0, 64, 0n, 100), 8);
  assert.equal(imports.fd_readdir(3, 0, 64, 0n, 100), 29);
  new Uint8Array(memory.buffer).set(new TextEncoder().encode("x"), 10);
  assert.equal(imports.path_open(3, 0, 10, 1, 0, 0n, 0n, 0, 20), 44);
  assert.equal(imports.fd_readdir(9, 0, 64, 0n, 100), 8);
  // Descriptors the runner did not track still reach the original WASI.
  assert.equal(imports.path_open(5, 0, 10, 1, 0, 0n, 0n, 0, 20), 44);
});
