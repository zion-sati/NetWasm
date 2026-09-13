import assert from "node:assert/strict";
import test from "node:test";

import { createApplicationProviderLoader } from "./application-provider-loader.mjs";

const digest = character => character.repeat(64);
const modules = ["example:logging/logger@1.0.0", "example:metrics/counter@2.0.0"];

function artifact(index) {
  return {
    relativePath: `imports/provider-${index}.mjs`,
    role: "application-import",
    mediaType: "text/javascript",
    sha256: digest(String(index + 1)),
    schemaVersion: null,
  };
}

function binding() {
  return {
    manifest: { artifacts: [artifact(0), artifact(1)] },
    request: {
      applicationImports: modules.map((module, index) => ({
        module,
        artifactPath: artifact(index).relativePath,
        sha256: artifact(index).sha256,
      })),
    },
    platformProviders: [],
    applicationProviders: modules.map(module => ({ module, functions: [] })),
  };
}

function createLoader(overrides = {}) {
  return createApplicationProviderLoader({
    readArtifact: async value => Uint8Array.of(Number(value.sha256[0])),
    hashBytes: async bytes => digest(String(bytes[0])),
    importModule: async bytes => Object.freeze({ value: bytes[0] }),
    ...overrides,
  });
}

test("verifies the complete selected closure before importing immutable provider sources", async () => {
  const calls = [];
  const signal = new AbortController().signal;
  const namespaces = [Object.freeze({ log() {} }), Object.freeze({ increment() {} })];
  const value = binding();
  const load = createLoader({
    async readArtifact(item, observedSignal) {
      calls.push(`read:${item.relativePath}`);
      assert.strictEqual(observedSignal, signal);
      return Uint8Array.of(Number(item.sha256[0]));
    },
    async hashBytes(bytes) {
      calls.push(`hash:${bytes[0]}`);
      return digest(String(bytes[0]));
    },
    async importModule(bytes, item) {
      calls.push(`import:${item.relativePath}:${bytes[0]}`);
      return namespaces[bytes[0] - 1];
    },
  });
  assert.equal(Object.isFrozen(load), true);
  const sources = await load({ binding: value, signal });
  assert.deepEqual(calls, [
    "read:imports/provider-0.mjs",
    "hash:1",
    "read:imports/provider-1.mjs",
    "hash:2",
    "import:imports/provider-0.mjs:1",
    "import:imports/provider-1.mjs:2",
  ]);
  assert.equal(Object.isFrozen(sources), true);
  assert.equal(Object.isFrozen(sources[0]), true);
  assert.deepEqual(sources.map(item => item.module), modules);
  assert.strictEqual(sources[0].value, namespaces[0]);
  assert.strictEqual(sources[1].value, namespaces[1]);
  value.applicationProviders.length = 0;
  assert.equal(sources.length, 2);

  assert.deepEqual(await load({ binding: {
    ...binding(),
    manifest: { artifacts: [] },
    request: { applicationImports: [] },
    applicationProviders: [],
  }, signal: null }), []);
});

test("validates exact loader actions", () => {
  const valid = {
    readArtifact() {},
    hashBytes() {},
    importModule() {},
  };
  for (const value of invalidObjects(valid)) {
    assert.throws(
      () => createApplicationProviderLoader(value.value),
      value.shape ? /options shape/i : /options is invalid/i);
  }
  for (const key of Object.keys(valid)) {
    assert.throws(
      () => createApplicationProviderLoader({ ...valid, [key]: null }),
      new RegExp(`${key}.*required`, "i"));
  }
});

test("validates exact load requests, capability products and signals before I/O", async () => {
  const valid = { binding: binding(), signal: null };
  for (const value of invalidObjects(valid)) {
    await assert.rejects(
      createLoader()(value.value),
      value.shape ? /request shape/i : /request is invalid/i);
  }
  for (const signal of [1, {}, { aborted: false }, {
    aborted: false,
    addEventListener() {},
  }]) {
    await assert.rejects(createLoader()({ ...valid, signal }), /must be an AbortSignal/i);
  }
  for (const value of invalidObjects(binding())) {
    await assert.rejects(
      createLoader()({ binding: value.value, signal: null }),
      value.shape ? /capability binding shape/i : /capability binding is invalid/i);
  }
  for (const mutate of [
    value => { value.applicationProviders = null; },
    value => { value.manifest = null; },
    value => { value.manifest.artifacts = null; },
    value => { value.request = null; },
    value => { value.request.applicationImports = null; },
  ]) {
    const value = binding();
    mutate(value);
    await assert.rejects(
      createLoader()({ binding: value, signal: null }),
      /capability binding is incomplete/i);
  }
});

test("rejects malformed provider, request-binding and artifact inventories before I/O", async () => {
  const cases = [
    ["applicationProviders", 0, null, /provider metadata is invalid/i],
    ["applicationProviders", 0, { module: modules[0] }, /provider metadata shape/i],
    ["applicationProviders", 0, { module: "", functions: [] }, /metadata module is invalid/i],
    ["applicationImports", 0, null, /import binding is invalid/i],
    ["applicationImports", 0, { module: modules[0] }, /import binding shape/i],
    ["applicationImports", 0, { module: "", artifactPath: "a", sha256: digest("1") }, /identity is invalid/i],
    ["artifacts", 0, null, /deployment artifact is invalid/i],
    ["artifacts", 0, { relativePath: "a" }, /deployment artifact shape/i],
    ["artifacts", 0, { ...artifact(0), relativePath: "" }, /identity is invalid/i],
  ];
  for (const [collection, index, replacement, pattern] of cases) {
    const value = binding();
    const owner = collection === "applicationProviders" ? value
      : collection === "applicationImports" ? value.request : value.manifest;
    owner[collection][index] = replacement;
    await assert.rejects(
      createLoader()({ binding: value, signal: null }),
      pattern);
  }

  const duplicateProvider = binding();
  duplicateProvider.applicationProviders.push({ ...duplicateProvider.applicationProviders[0] });
  await assert.rejects(
    createLoader()({ binding: duplicateProvider, signal: null }),
    /metadata module is invalid/i);
  for (const location of ["applicationImports", "artifacts"]) {
    const value = binding();
    const values = location === "applicationImports"
      ? value.request.applicationImports : value.manifest.artifacts;
    values.push({ ...values[0] });
    await assert.rejects(
      createLoader()({ binding: value, signal: null }),
      /identity is invalid/i);
  }
});

test("rejects missing or mismatched selected application bindings before I/O", async () => {
  const mutations = [
    [value => { value.request.applicationImports = value.request.applicationImports.slice(1); }, /no import binding/i],
    [value => { value.manifest.artifacts = value.manifest.artifacts.slice(1); }, /no matching artifact/i],
    [value => { value.manifest.artifacts[0].role = "asset"; }, /no matching artifact/i],
    [value => { value.manifest.artifacts[0].mediaType = "application/javascript"; }, /no matching artifact/i],
    [value => { value.manifest.artifacts[0].sha256 = digest("9"); }, /no matching artifact/i],
  ];
  for (const [mutate, pattern] of mutations) {
    let reads = 0;
    const value = binding();
    mutate(value);
    await assert.rejects(createLoader({
      readArtifact() { reads++; },
    })({ binding: value, signal: null }), pattern);
    assert.equal(reads, 0);
  }
});

test("rejects transport and integrity failures before evaluating any module", async () => {
  let imports = 0;
  await assert.rejects(createLoader({
    readArtifact: async () => null,
    importModule: async () => { imports++; },
  })({ binding: binding(), signal: null }), /transport returned invalid bytes/i);
  assert.equal(imports, 0);

  for (const digestValue of [null, digest("9")]) {
    imports = 0;
    await assert.rejects(createLoader({
      hashBytes: async () => digestValue,
      importModule: async () => { imports++; },
    })({ binding: binding(), signal: null }), /integrity validation/i);
    assert.equal(imports, 0);
  }

  const calls = [];
  await assert.rejects(createLoader({
    async readArtifact(item) {
      calls.push(`read:${item.relativePath}`);
      return Uint8Array.of(Number(item.sha256[0]));
    },
    async hashBytes(bytes) {
      calls.push(`hash:${bytes[0]}`);
      return bytes[0] === 1 ? digest("1") : digest("9");
    },
    async importModule() { calls.push("import"); return {}; },
  })({ binding: binding(), signal: null }), /integrity validation/i);
  assert.deepEqual(calls, [
    "read:imports/provider-0.mjs", "hash:1",
    "read:imports/provider-1.mjs", "hash:2",
  ]);
});

test("rejects invalid imported namespaces and preserves import failures", async () => {
  for (const namespace of [null, 1, [], () => {}]) {
    await assert.rejects(
      createLoader({ importModule: async () => namespace })({ binding: binding(), signal: null }),
      /invalid namespace/i);
  }
  const failure = new Error("module failed");
  await assert.rejects(
    createLoader({ importModule: async () => { throw failure; } })({ binding: binding(), signal: null }),
    error => error === failure);
});

test("prevents module evaluation when cancellation is observed between stages", async () => {
  const before = new AbortController();
  before.abort();
  let reads = 0;
  await assert.rejects(createLoader({
    readArtifact: async () => { reads++; },
  })({ binding: binding(), signal: before.signal }), /loading was cancelled/i);
  assert.equal(reads, 0);

  const afterRead = new AbortController();
  let imports = 0;
  await assert.rejects(createLoader({
    async hashBytes(bytes) {
      afterRead.abort();
      return digest(String(bytes[0]));
    },
    async importModule() { imports++; return {}; },
  })({ binding: binding(), signal: afterRead.signal }), /loading was cancelled/i);
  assert.equal(imports, 0);

  const afterImport = new AbortController();
  await assert.rejects(createLoader({
    async importModule() {
      afterImport.abort();
      return {};
    },
  })({ binding: binding(), signal: afterImport.signal }), /loading was cancelled/i);
});

function invalidObjects(base) {
  const withSymbol = { ...base, [Symbol("hidden")]: true };
  const inherited = Object.assign(Object.create({ inherited: true }), base);
  const keys = Object.keys(base);
  const wrongKey = { ...base };
  delete wrongKey[keys[0]];
  wrongKey.other = null;
  const nonEnumerable = { ...base };
  Object.defineProperty(nonEnumerable, keys[0], { value: base[keys[0]], enumerable: false });
  const accessor = { ...base };
  Object.defineProperty(accessor, keys[0], { get: () => base[keys[0]], enumerable: true });
  return [
    { value: null, shape: false },
    { value: 1, shape: false },
    { value: [], shape: false },
    { value: withSymbol, shape: false },
    { value: inherited, shape: true },
    { value: { ...base, extra: true }, shape: true },
    { value: wrongKey, shape: true },
    { value: nonEnumerable, shape: true },
    { value: accessor, shape: true },
  ];
}
