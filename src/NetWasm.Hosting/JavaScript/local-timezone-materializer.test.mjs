import assert from "node:assert/strict";
import { createHash } from "node:crypto";
import { mkdir, mkdtemp, rm, writeFile } from "node:fs/promises";
import { tmpdir } from "node:os";
import { join } from "node:path";
import test from "node:test";
import { createLocalTimeZoneMaterializer } from "./local-timezone-materializer.mjs";
import { selectTimeZoneSidecar } from "./timezone-sidecar-selector.mjs";

const bytes = Uint8Array.of(5, 6, 7);

test("loads and mounts the manifested local sidecar relative to the manifest", async () => {
  const root = await mkdtemp(join(tmpdir(), "netwasm-local-timezone-"));
  try {
    await mkdir(join(root, "nested dir"));
    await writeFile(join(root, "nested dir", "app.wasm.tz-info"), bytes);
    const calls = [];
    const materialize = createLocalTimeZoneMaterializer({
      manifestPath: join(root, "app.netwasm.deployment.json"),
      mountReadOnlyFile(request) {
        calls.push(request);
        return () => calls.push("released");
      },
    });
    const signal = new AbortController().signal;
    const action = await materialize({ selection: selection(), signal });
    assert.equal(Object.isFrozen(materialize), true);
    assert.deepEqual(calls[0].bytes, bytes);
    assert.equal(calls[0].guestPath, "/netwasm-timezones/netwasm-timezones.nwtz");
    await action.release();
    assert.equal(calls[1], "released");
  } finally {
    await rm(root, { recursive: true, force: true });
  }
});

test("skips local filesystem access for a null selection and reports a missing selected sidecar", async () => {
  const manifestPath = join(tmpdir(), "missing", "app.netwasm.deployment.json");
  let mounts = 0;
  const materialize = createLocalTimeZoneMaterializer({
    manifestPath,
    mountReadOnlyFile() { mounts++; return () => {}; },
  });
  assert.equal(await materialize({ selection: null }), null);
  assert.equal(mounts, 0);

  const tampered = selection();
  const invalid = {
    ...tampered,
    artifact: { ...tampered.artifact, sha256: "0".repeat(64) },
  };
  await assert.rejects(() => materialize({ selection: invalid }), /ENOENT/);
  assert.equal(mounts, 0);
});

test("validates local timezone composition and requests before filesystem access", () => {
  const manifestPath = join(tmpdir(), "app.netwasm.deployment.json");
  const mountReadOnlyFile = () => () => {};
  for (const options of [null, 1, [], {}, { manifestPath, mountReadOnlyFile, extra: true },
    Object.create({ manifestPath, mountReadOnlyFile })]) {
    assert.throws(() => createLocalTimeZoneMaterializer(options), /options/);
  }
  const withSymbol = { manifestPath, mountReadOnlyFile, [Symbol("invalid")]: true };
  assert.throws(() => createLocalTimeZoneMaterializer(withSymbol), /options/);
  const withAccessor = { manifestPath };
  Object.defineProperty(withAccessor, "mountReadOnlyFile", {
    get: () => mountReadOnlyFile,
    enumerable: true,
  });
  assert.throws(() => createLocalTimeZoneMaterializer(withAccessor), /options/);
  assert.throws(
    () => createLocalTimeZoneMaterializer({ manifestPath, mountReadOnlyFile: null }),
    /mount act/);
  assert.throws(
    () => createLocalTimeZoneMaterializer({ manifestPath: "relative.json", mountReadOnlyFile }),
    /canonical and absolute/);

  const materialize = createLocalTimeZoneMaterializer({ manifestPath, mountReadOnlyFile });
  for (const request of [null, 1, [], {}, { selection: null, extra: true },
    Object.create({ selection: null })]) {
    assert.throws(() => materialize(request), /request/);
  }
  const requestWithSymbol = { selection: null, [Symbol("invalid")]: true };
  assert.throws(() => materialize(requestWithSymbol), /request/);
  const requestWithAccessor = {};
  Object.defineProperty(requestWithAccessor, "selection", {
    get: () => null,
    enumerable: true,
  });
  assert.throws(() => materialize(requestWithAccessor), /request/);
});

function selection() {
  const application = artifact("nested dir/app.wasm", "application", "application/wasm", "a".repeat(64), null);
  const timezone = artifact(
    "nested dir/app.wasm.tz-info",
    "timezone-data",
    "application/octet-stream",
    createHash("sha256").update(bytes).digest("hex"),
    1);
  return selectTimeZoneSidecar({
    runtimeFeatures: ["local-time"],
    artifacts: [application, timezone],
    environment: [{ name: "TZ", value: "Australia/Melbourne" }],
  });
}

function artifact(relativePath, role, mediaType, sha256, schemaVersion) {
  return { relativePath, role, mediaType, sha256, schemaVersion };
}
