import assert from "node:assert/strict";
import test from "node:test";
import { createBrowserVerifiedModuleImporter } from "./browser-verified-module-importer.mjs";

const bytes = new TextEncoder().encode("export const value = 41;\n");

test("imports verified browser bytes and always revokes the temporary URL", async () => {
  const calls = [];
  const importer = createBrowserVerifiedModuleImporter({
    createModuleUrl(value, mediaType) {
      assert.deepEqual(value, bytes);
      assert.equal(mediaType, "text/javascript");
      calls.push("create");
      return "blob:netwasm/1";
    },
    async importModule(url) {
      calls.push(`import:${url}`);
      return { value: 41 };
    },
    revokeModuleUrl(url) { calls.push(`revoke:${url}`); },
  });
  assert.equal(Object.isFrozen(importer), true);
  assert.deepEqual(await importer(bytes, { mediaType: "text/javascript" }), { value: 41 });
  assert.deepEqual(calls, ["create", "import:blob:netwasm/1", "revoke:blob:netwasm/1"]);

  const failed = createBrowserVerifiedModuleImporter({
    createModuleUrl: () => "blob:netwasm/failed",
    importModule: async () => { throw new Error("private failure"); },
    revokeModuleUrl: url => calls.push(`failed-revoke:${url}`),
  });
  await assert.rejects(() => failed(bytes, { mediaType: "text/javascript" }), /private failure/);
  assert.equal(calls.at(-1), "failed-revoke:blob:netwasm/failed");
});

test("validates its exact factory and invocation inputs", async () => {
  const valid = {
    createModuleUrl: () => "blob:valid",
    importModule: async () => ({}),
    revokeModuleUrl() {},
  };
  for (const options of [null, 1, [], {}, { ...valid, extra: true }, Object.create(valid)]) {
    assert.throws(() => createBrowserVerifiedModuleImporter(options), /options/);
  }
  const withSymbol = { ...valid, [Symbol("bad")]: true };
  assert.throws(() => createBrowserVerifiedModuleImporter(withSymbol), /options/);
  const withAccessor = { ...valid };
  Object.defineProperty(withAccessor, "importModule", { enumerable: true, get: () => async () => ({}) });
  assert.throws(() => createBrowserVerifiedModuleImporter(withAccessor), /options/);
  for (const key of Object.keys(valid)) {
    assert.throws(() => createBrowserVerifiedModuleImporter({ ...valid, [key]: null }), new RegExp(key));
  }

  const importer = createBrowserVerifiedModuleImporter(valid);
  for (const value of [null, new ArrayBuffer(1), [], "bytes"]) {
    await assert.rejects(() => importer(value, { mediaType: "text/javascript" }), /bytes/);
  }
  for (const artifact of [null, 1, {}, { mediaType: null }, { mediaType: "" }]) {
    await assert.rejects(() => importer(bytes, artifact), /artifact/);
  }
  for (const url of [null, "", "https://example.test/module.mjs", "Blob:wrong-case"]) {
    const invalid = createBrowserVerifiedModuleImporter({ ...valid, createModuleUrl: () => url });
    await assert.rejects(() => invalid(bytes, { mediaType: "text/javascript" }), /URL factory/);
  }
});
