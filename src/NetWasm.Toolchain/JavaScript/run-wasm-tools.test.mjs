import assert from "node:assert/strict";
import { mkdtemp, rm, writeFile } from "node:fs/promises";
import { tmpdir } from "node:os";
import path from "node:path";
import { spawnSync } from "node:child_process";
import test from "node:test";
import { fileURLToPath } from "node:url";

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
