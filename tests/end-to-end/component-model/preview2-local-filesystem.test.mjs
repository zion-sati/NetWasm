import assert from "node:assert/strict";
import { createHash } from "node:crypto";
import { mkdtemp, rm, writeFile } from "node:fs/promises";
import { tmpdir } from "node:os";
import { join } from "node:path";
import test from "node:test";
import * as localFilesystem from "@bytecodealliance/preview2-shim/filesystem";
import { createPreview2Filesystem } from "../../../src/NetWasm.Hosting/JavaScript/preview2-filesystem.mjs";
import { createLocalTimeZoneMaterializer } from "../../../src/NetWasm.Hosting/JavaScript/local-timezone-materializer.mjs";
import { executeWithTimeZoneResources } from "../../../src/NetWasm.Hosting/JavaScript/timezone-execution.mjs";
import { normalExecutionResult } from "../../../src/NetWasm.Hosting/JavaScript/execution-result.mjs";

// Keep local and browser exports in separate test processes: ESM caches an
// already resolved specifier before a later resolution hook can change it.
test("the updated local package opens and reads isolated host preopens", async () => {
  const directory = await mkdtemp(join(tmpdir(), "netwasm-shim-contract-"));
  const before = localFilesystem.preopens.getDirectories();
  let root;
  let file;
  try {
    await writeFile(join(directory, "asset"), Uint8Array.of(44, 55));
    const filesystem = localFilesystem.createFilesystem({ preopens: { "/data": directory } });
    [[root]] = filesystem.preopens.getDirectories();
    assert.ok(root instanceof filesystem.types.Descriptor);
    file = root.openAt({}, "asset", {}, { read: true });
    const [bytes] = file.read(2n, 0n);
    assert.deepEqual(bytes, Uint8Array.of(44, 55));
    assert.deepEqual(localFilesystem.preopens.getDirectories(), before);
  } finally {
    file?.[Symbol.dispose]();
    root?.[Symbol.dispose]();
    await rm(directory, { recursive: true, force: true });
  }
});

test("the same production provider retains native consumer writes beside its internal file", async () => {
  const directory = await mkdtemp(join(tmpdir(), "netwasm-provider-contract-"));
  let sourceRoot;
  let fs;
  try {
    await writeFile(join(directory, "user.txt"), Uint8Array.of(1, 2, 3));
    const source = localFilesystem.createFilesystem({ preopens: { "/consumer": directory } });
    [[sourceRoot]] = source.preopens.getDirectories();
    fs = createPreview2Filesystem({ filesystem: source });
    fs.mountReadOnlyFile({ guestPath: "/internal/asset", bytes: Uint8Array.of(4, 5, 6) });
    const [[consumer], [internal]] = fs.preopens.getDirectories();
    const userFile = consumer.openAt({}, "user.txt", {}, { read: true, write: true });
    const asset = internal.openAt({}, "asset", {}, { read: true });
    assert.ok(userFile instanceof fs.types.Descriptor);
    assert.ok(asset instanceof fs.types.Descriptor);
    assert.equal(userFile.write(Uint8Array.of(9), 0n), 1n);
    assert.deepEqual(userFile.read(3n, 0n)[0], Uint8Array.of(9, 2, 3));
    assert.deepEqual(asset.read(3n, 0n), [Uint8Array.of(4, 5, 6), true]);
    assert.equal(userFile.isSameObject(asset), false);
    assert.equal(asset.isSameObject(userFile), false);
    assert.throws(() => consumer.renameAt("user.txt", internal, "copy"), value => value === "read-only");
    assert.throws(() => asset.write(Uint8Array.of(0), 0n), value => value === "read-only");
    fs.dispose();
    assert.throws(() => userFile.read(1n, 0n), value => value === "bad-descriptor");
    assert.throws(() => asset.read(1n, 0n), value => value === "bad-descriptor");
    assert.equal(source.preopens.getDirectories().length, 1);
  } finally {
    fs?.dispose();
    sourceRoot?.[Symbol.dispose]();
    await rm(directory, { recursive: true, force: true });
  }
});

test("local execution loads the adjacent verified sidecar and releases its scope on every outcome", async () => {
  const directory = await mkdtemp(join(tmpdir(), "netwasm-local-timezone-contract-"));
  const bytes = Uint8Array.of(4, 5, 6);
  const sha256 = createHash("sha256").update(bytes).digest("hex");
  try {
    await writeFile(join(directory, "program.wasm.tz-info"), bytes);
    await writeFile(join(directory, "user.txt"), Uint8Array.of(1));
    for (const mode of ["selected", "utc", "integrity", "execution-failure"]) {
      const calls = [];
      const source = localFilesystem.createFilesystem({ preopens: { "/consumer": directory } });
      const [[sourceRoot]] = source.preopens.getDirectories();
      let asset;
      let consumerFile;
      let provider;
      try {
        const outcome = await executeWithTimeZoneResources({
          selection: mode === "utc" ? null : {
            timeZone: "Australia/Melbourne",
            artifact: { relativePath: "program.wasm.tz-info", role: "timezone-data", mediaType: "application/octet-stream",
              sha256: mode === "integrity" ? "0".repeat(64) : sha256, schemaVersion: 1 },
          },
          createFilesystem() {
            provider = createPreview2Filesystem({ filesystem: source });
            return provider;
          },
          createMaterializer: mountReadOnlyFile => createLocalTimeZoneMaterializer({
            manifestPath: join(directory, "deployment.json"), mountReadOnlyFile,
          }),
          async execute({ filesystem }) {
            calls.push("execute");
            const entries = filesystem.preopens.getDirectories();
            const [[consumer]] = entries;
            consumerFile = consumer.openAt({}, "user.txt", {}, { read: true, write: true });
            assert.equal(consumerFile.write(Uint8Array.of(9), 0n), 1n);
            if (mode === "utc") {
              assert.equal(entries.length, 1);
            } else {
              assert.equal(entries[1][1], "/netwasm-timezones");
              asset = entries[1][0].openAt({}, "netwasm-timezones.nwtz", {}, { read: true });
              assert.deepEqual(asset.read(3n, 0n), [bytes, true]);
            }
            if (mode === "execution-failure") throw new Error("private execution detail");
            return normalExecutionResult(0);
          },
          releaseActions: [{ code: "host.source", message: "Could not release the source filesystem.", release() {
            calls.push("caller-release");
            assert.throws(() => provider.preopens.getDirectories(), /disposed/);
            assert.equal(sourceRoot.getType(), "directory");
            sourceRoot[Symbol.dispose]();
          } }],
        });
        const failed = mode === "integrity" || mode === "execution-failure";
        assert.equal(outcome.completionKind, failed ? "hostFailure" : "normal");
        assert.equal(outcome.primaryFailure?.code ?? null, mode === "integrity" ? "host.timezone-materialize"
          : mode === "execution-failure" ? "host.deployment-execute" : null);
        assert.deepEqual(calls, mode === "integrity" ? ["caller-release"] : ["execute", "caller-release"]);
        assert.deepEqual(outcome.cleanupFailures, []);
        if (asset) assert.throws(() => asset.read(1n, 0n), value => value === "bad-descriptor");
        if (consumerFile) assert.throws(() => consumerFile.read(1n, 0n), value => value === "bad-descriptor");
      } finally {
        provider?.dispose();
        sourceRoot[Symbol.dispose]();
      }
    }
  } finally {
    await rm(directory, { recursive: true, force: true });
  }
});
