import assert from "node:assert/strict";
import { createHash } from "node:crypto";
import { readFile } from "node:fs/promises";
import { registerHooks } from "node:module";
import test from "node:test";
import { createReadOnlyFileDirectory } from "../../../src/NetWasm.Hosting/JavaScript/read-only-file-directory.mjs";
import { createPreview2Filesystem } from "../../../src/NetWasm.Hosting/JavaScript/preview2-filesystem.mjs";

// Exercise the public browser export conditions without depending on private
// package paths. Actual browser execution remains a separate release gate.
const browserNodeImports = [];
const browserResolution = registerHooks({
  resolve(specifier, context, nextResolve) {
    if (specifier.startsWith("node:")) browserNodeImports.push(specifier);
    return nextResolve(specifier, specifier.startsWith("@bytecodealliance/preview2-shim")
      ? { ...context, conditions: context.conditions.filter(value => value !== "node") }
      : context);
  },
});
let browserFilesystem;
let WASIShim;
try {
  browserFilesystem = await import("@bytecodealliance/preview2-shim/filesystem");
  ({ WASIShim } = await import("@bytecodealliance/preview2-shim/instantiation"));
} finally {
  browserResolution.deregister();
}

test("the pinned Toolchain package binds the exact updated dependency lock", async () => {
  const root = new URL("../../../", import.meta.url);
  const lockBytes = await readFile(new URL("package-lock.json", root));
  const lock = JSON.parse(lockBytes);
  const pins = JSON.parse(await readFile(new URL("eng/toolchain.json", root)));
  const manifest = JSON.parse(await readFile(new URL(
    "src/NetWasm.Toolchain/toolchain-manifest.json", root)));
  const assets = new Map(manifest.assets.map(asset => [asset.id, asset]));
  assert.equal(lock.packages["node_modules/@bytecodealliance/preview2-shim"].version,
    pins.preview2Shim);
  assert.equal(lock.packages["node_modules/@bytecodealliance/jco"].version, pins.jco);
  assert.equal(manifest.packageId, "NetWasm.Toolchain");
  assert.equal(assets.get("jco.package").version, pins.jco);
  assert.equal(assets.get("preview2-shim.package").version, pins.preview2Shim);
  assert.equal(assets.get("jco.package-lock").sha256,
    createHash("sha256").update(lockBytes).digest("hex"));
});

test("browser filesystem namespaces isolate mounts and wrap adapter resources", () => {
  assert.deepEqual(browserNodeImports, []);
  const before = browserFilesystem.preopens.getDirectories();
  const first = createAdapter(11);
  const second = createAdapter(22);
  const a = browserFilesystem.createFilesystem({ adapter: first, preopens: { "/data": "first" } });
  const b = browserFilesystem.createFilesystem({ adapter: second, preopens: { "/data": "second" } });
  try {
    const [[root, path]] = a.preopens.getDirectories();
    assert.equal(path, "/data");
    assert.ok(root instanceof a.types.Descriptor);
    const file = root.openAt({}, "asset", {}, { read: true });
    assert.ok(file instanceof a.types.Descriptor);
    assert.deepEqual(file.read(1n, 0n), [Uint8Array.of(11), true]);
    assert.deepEqual(b.preopens.getDirectories()[0][0]
      .openAt({}, "asset", {}, { read: true }).read(1n, 0n), [Uint8Array.of(22), true]);
    assert.deepEqual(first.capabilities, ["first"]);
    assert.deepEqual(second.capabilities, ["second"]);
    assert.deepEqual(browserFilesystem.preopens.getDirectories(), before);

    a.dispose();
    a.dispose();
    assert.equal(first.disposals, 1);
    assert.throws(() => a.preopens.getDirectories());
    // Revoking previously opened resources is the adapter's responsibility.
    assert.throws(() => file.read(1n, 0n), value => value === "bad-descriptor");
    assert.deepEqual(b.preopens.getDirectories()[0][0]
      .openAt({}, "asset", {}, { read: true }).read(1n, 0n), [Uint8Array.of(22), true]);
  } finally {
    a.dispose();
    b.dispose();
  }
  assert.equal(second.disposals, 1);
});

test("WASIShim accepts explicit filesystem namespaces and preserves every preopen", () => {
  const adapter = createAdapter(33);
  const filesystem = browserFilesystem.createFilesystem({
    adapter,
    preopens: { "/netwasm-timezones": "internal", "/application": "consumer" },
  });
  try {
    const shim = new WASIShim({
      filesystem,
      environment: { TZ: "Australia/Melbourne" },
      arguments: ["application"],
      sandbox: { enableNetwork: false },
    });
    const imports = shim.getImportObject({ asVersion: "0.2.11" });
    assert.equal(imports["wasi:filesystem/types@0.2.11"], filesystem.types);
    assert.equal(imports["wasi:filesystem/preopens@0.2.11"], filesystem.preopens);
    assert.deepEqual(filesystem.preopens.getDirectories().map(([, path]) => path),
      ["/netwasm-timezones", "/application"]);
    assert.deepEqual(adapter.capabilities, ["internal", "consumer"]);
    assert.deepEqual(imports["wasi:cli/environment@0.2.11"].getEnvironment(),
      [["TZ", "Australia/Melbourne"]]);
  } finally {
    filesystem.dispose();
  }
});

test("the upstream browser factory preserves the production read-only resource contract", () => {
  const owner = createReadOnlyFileDirectory({
    fileName: "netwasm-timezones.nwtz",
    bytes: Uint8Array.of(10, 20, 30),
  });
  const filesystem = browserFilesystem.createFilesystem({
    adapter: { getRoot: capability => capability, dispose: owner.release },
    preopens: { "/netwasm-timezones": owner.directory },
  });
  try {
    const [[directory, path]] = filesystem.preopens.getDirectories();
    assert.equal(path, "/netwasm-timezones");
    const file = directory.openAt({}, "netwasm-timezones.nwtz", {}, { read: true });
    assert.ok(file instanceof filesystem.types.Descriptor);
    assert.equal(file.getType(), "regular-file");
    assert.deepEqual(file.read(2n, 1n), [Uint8Array.of(20, 30), true]);
    assert.equal(file.isSameObject(directory.openAt({}, "netwasm-timezones.nwtz", {}, { read: true })), true);
    assert.throws(() => file.write(Uint8Array.of(0), 0n), value => value === "read-only");
    assert.throws(() => directory.openAt({}, "netwasm-timezones.nwtz", {}, { write: true }),
      value => value === "read-only");
    filesystem.dispose();
    assert.throws(() => file.read(1n, 0n), value => value === "bad-descriptor");
  } finally {
    filesystem.dispose();
  }
});

test("the production provider combines browser consumer files and a read-only mount", () => {
  const source = browserFilesystem.createFilesystem({
    adapter: new browserFilesystem.InMemoryFilesystemAdapter(),
    preopens: { "/consumer": { dir: { "user.txt": { source: Uint8Array.of(1, 2, 3) } } } },
  });
  const fs = createPreview2Filesystem({ filesystem: source });
  try {
    fs.mountReadOnlyFile({ guestPath: "/internal/asset", bytes: Uint8Array.of(4, 5, 6) });
    const [[consumer], [internal]] = fs.preopens.getDirectories();
    const userFile = consumer.openAt({}, "user.txt", {}, { read: true, write: true });
    const asset = internal.openAt({}, "asset", {}, { read: true });
    assert.ok(userFile instanceof fs.types.Descriptor);
    assert.ok(asset instanceof fs.types.Descriptor);
    assert.deepEqual(userFile.read(3n, 0n), [Uint8Array.of(1, 2, 3), true]);
    assert.equal(userFile.write(Uint8Array.of(9), 0n), 1n);
    assert.deepEqual(userFile.read(3n, 0n), [Uint8Array.of(9, 2, 3), true]);
    assert.deepEqual(asset.read(3n, 0n), [Uint8Array.of(4, 5, 6), true]);
    assert.equal(userFile.isSameObject(asset), false);
    assert.equal(asset.isSameObject(userFile), false);
    assert.throws(() => consumer.linkAt({}, "user.txt", internal, "copy"), value => value === "read-only");
    assert.throws(() => asset.write(Uint8Array.of(0), 0n), value => value === "read-only");
    const directoryStream = consumer.readDirectory();
    assert.ok(directoryStream instanceof fs.types.DirectoryEntryStream);
    assert.equal(directoryStream.readDirectoryEntry().name, "user.txt");
    fs.dispose();
    assert.throws(() => asset.read(1n, 0n), value => value === "bad-descriptor");
    assert.throws(() => userFile.read(1n, 0n), value => value === "bad-descriptor");
    assert.equal(source.preopens.getDirectories().length, 1);
  } finally {
    fs.dispose();
    source.dispose();
  }
});

function createAdapter(value) {
  let disposed = false;
  const adapter = {
    capabilities: [],
    disposals: 0,
    getRoot(capability) {
      adapter.capabilities.push(capability);
      return {
        openAt(pathFlags, path, openFlags, flags) {
          assert.deepEqual([pathFlags, path, openFlags, flags], [{}, "asset", {}, { read: true }]);
          return {
            read(length, offset) {
              if (disposed) throw "bad-descriptor";
              assert.deepEqual([length, offset], [1n, 0n]);
              return [Uint8Array.of(value), true];
            },
          };
        },
      };
    },
    dispose() {
      disposed = true;
      adapter.disposals++;
    },
  };
  return adapter;
}
