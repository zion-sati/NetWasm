import assert from "node:assert/strict";
import test from "node:test";

import {
  createVerifiedDeploymentManifestLoader,
} from "./verified-deployment-manifest-loader.mjs";
import { isContractError } from "./contract-error.mjs";

const encoder = new TextEncoder();
const bytes = encoder.encode("manifest");
const digest = "1".repeat(64);
const manifest = Object.freeze({ schemaVersion: 1 });

function options(overrides = {}) {
  return {
    async readManifestBytes() { return bytes; },
    async hashBytes(value) {
      assert.notEqual(value, bytes);
      assert.deepEqual(value, bytes);
      return digest;
    },
    readManifest(text) {
      assert.equal(text, "manifest");
      return manifest;
    },
    ...overrides,
  };
}

test("reads, copies, verifies, decodes, and validates one manifest", async () => {
  const load = createVerifiedDeploymentManifestLoader(options());
  assert.equal(Object.isFrozen(load), true);
  assert.equal(await load({ expectedSha256: digest, signal: null }), manifest);
});

test("observes cancellation before transport and after each asynchronous act", async () => {
  for (const stage of ["before", "read", "hash"]) {
    const controller = new AbortController();
    const calls = [];
    if (stage === "before") controller.abort();
    const load = createVerifiedDeploymentManifestLoader(options({
      async readManifestBytes() {
        calls.push("read");
        if (stage === "read") controller.abort();
        return bytes;
      },
      async hashBytes() {
        calls.push("hash");
        if (stage === "hash") controller.abort();
        return digest;
      },
      readManifest() { calls.push("manifest"); return manifest; },
    }));
    await assert.rejects(
      () => load({ expectedSha256: digest, signal: controller.signal }),
      error => error.name === "AbortError");
    assert.deepEqual(calls, stage === "before" ? [] : stage === "read" ? ["read"] : ["read", "hash"]);
  }
});

test("rejects malformed factories and requests before transport", async () => {
  const valid = options();
  for (const value of [null, 1, [], {}, { ...valid, extra: () => {} }, Object.create(valid)]) {
    assert.throws(() => createVerifiedDeploymentManifestLoader(value), TypeError);
  }
  for (const name of Object.keys(valid)) {
    assert.throws(() => createVerifiedDeploymentManifestLoader({ ...valid, [name]: null }), /action/);
  }
  const symbolicFactory = { ...valid, [Symbol("invalid")]: true };
  assert.throws(() => createVerifiedDeploymentManifestLoader(symbolicFactory), TypeError);
  const factoryAccessor = { ...valid };
  Object.defineProperty(factoryAccessor, "hashBytes", { enumerable: true, get: () => valid.hashBytes });
  assert.throws(() => createVerifiedDeploymentManifestLoader(factoryAccessor), TypeError);

  let reads = 0;
  const load = createVerifiedDeploymentManifestLoader(options({
    readManifestBytes() { reads++; return bytes; },
  }));
  const request = { expectedSha256: digest, signal: null };
  for (const value of [null, 1, [], {}, { ...request, extra: true }, Object.create(request)]) {
    await assert.rejects(() => load(value), TypeError);
  }
  for (const expectedSha256 of [null, "A".repeat(64), "1".repeat(63)]) {
    await assert.rejects(() => load({ ...request, expectedSha256 }), isContractError);
  }
  for (const signal of [1, {}, { aborted: false, addEventListener() {} }]) {
    await assert.rejects(() => load({ ...request, signal }), /AbortSignal/);
  }
  const symbolicRequest = { ...request, [Symbol("invalid")]: true };
  await assert.rejects(() => load(symbolicRequest), TypeError);
  const requestAccessor = { ...request };
  Object.defineProperty(requestAccessor, "signal", { enumerable: true, get: () => null });
  await assert.rejects(() => load(requestAccessor), TypeError);
  assert.equal(reads, 0);
});

test("rejects invalid transport, digest, UTF-8, and reader products", async () => {
  const request = { expectedSha256: digest, signal: null };
  for (const value of [null, new ArrayBuffer(1), []]) {
    const load = createVerifiedDeploymentManifestLoader(options({
      readManifestBytes: async () => value,
    }));
    await assert.rejects(() => load(request), /transport/);
  }
  for (const value of [null, "A".repeat(64), "1".repeat(63)]) {
    const load = createVerifiedDeploymentManifestLoader(options({
      hashBytes: async () => value,
    }));
    await assert.rejects(() => load(request), /hasher/);
  }
  await assert.rejects(
    () => createVerifiedDeploymentManifestLoader(options({
      hashBytes: async () => "2".repeat(64),
    }))(request),
    isContractError);
  await assert.rejects(
    () => createVerifiedDeploymentManifestLoader(options({
      readManifestBytes: async () => Uint8Array.of(0xff),
      hashBytes: async () => digest,
    }))(request),
    isContractError);
  await assert.rejects(
    () => createVerifiedDeploymentManifestLoader(options({
      readManifest: () => { throw new TypeError("private schema detail"); },
    }))(request),
    isContractError);
  for (const value of [null, [], {}, Object.freeze([])]) {
    const load = createVerifiedDeploymentManifestLoader(options({
      readManifest: () => value,
    }));
    await assert.rejects(() => load(request), /reader/);
  }
});
