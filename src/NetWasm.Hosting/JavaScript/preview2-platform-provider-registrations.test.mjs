import assert from "node:assert/strict";
import test from "node:test";

import { platformProviderCatalog } from "./platform-provider-catalog.generated.mjs";
import {
  createPreview2PlatformProviderRegistrations,
} from "./preview2-platform-provider-registrations.mjs";
import { resourceDisposeSymbol } from "./resource-disposal.mjs";

const stdout = Object.freeze({ write() {} });
const stderr = Object.freeze({ write() {} });

function sourceRequest(overrides = {}) {
  return Object.freeze({
    arguments: Object.freeze(["application", "--value"]),
    environment: Object.freeze([
      Object.freeze({ name: "ALPHA", value: "one" }),
    ]),
    filesystem: null,
    grants: Object.freeze({
      clocks: Object.freeze(["wall"]),
      environment: Object.freeze(["ALPHA"]),
      network: "denyAll",
      preopens: Object.freeze([]),
      randomness: false,
    }),
    signal: null,
    stderr,
    stdout,
    ...overrides,
  });
}

function sourcesForAllProviders() {
  class OutputStream {}
  class Pollable {}
  const sources = Object.fromEntries(platformProviderCatalog.providers.map(
    provider => [provider.module, Object.freeze({ module: provider.module })]));
  sources["wasi:io/streams@0.2.11"] = Object.freeze({ OutputStream });
  sources["wasi:io/poll@0.2.11"] = Object.freeze({ Pollable });
  return sources;
}

test("creates one isolated shim and import object for one execution request", () => {
  const calls = [];
  const stdoutWrites = [];
  const stderrWrites = [];
  const sources = sourcesForAllProviders();
  const registrations = createPreview2PlatformProviderRegistrations({
    catalog: platformProviderCatalog,
    createShim(config) {
      calls.push({ act: "shim", config });
      return {
        getImportObject(options) {
          calls.push({ act: "imports", options });
          return sources;
        },
      };
    },
  });
  const request = sourceRequest({
    stdout: Object.freeze({ write: bytes => stdoutWrites.push([...bytes]) }),
    stderr: Object.freeze({ write: bytes => stderrWrites.push([...bytes]) }),
  });

  assert.equal(Object.isFrozen(registrations), true);
  assert.equal(registrations.length, 27);
  assert.equal(Object.isFrozen(registrations[0]), true);
  assert.strictEqual(registrations[0].provider, platformProviderCatalog.providers[0]);
  assert.strictEqual(registrations[0].createSource(request),
    sources[registrations[0].provider.module]);
  assert.strictEqual(registrations[1].createSource(request),
    sources[registrations[1].provider.module]);
  const byModule = new Map(registrations.map(value => [value.provider.module, value]));
  const stdoutSource = byModule.get("wasi:cli/stdout@0.2.11").createSource(request);
  const stderrSource = byModule.get("wasi:cli/stderr@0.2.11").createSource(request);
  const stdoutStream = stdoutSource.getStdout();
  const stderrStream = stderrSource.getStderr();
  assert.equal(stdoutStream instanceof sources["wasi:io/streams@0.2.11"].OutputStream, true);
  stdoutStream.checkWrite();
  stdoutStream.write(Uint8Array.of(65));
  stderrStream.blockingWriteAndFlush(Uint8Array.of(66));
  stdoutStream[resourceDisposeSymbol]();
  stderrStream[resourceDisposeSymbol]();
  const nextStdoutStream = stdoutSource.getStdout();
  const nextStderrStream = stderrSource.getStderr();
  assert.notStrictEqual(nextStdoutStream, stdoutStream);
  assert.notStrictEqual(nextStderrStream, stderrStream);
  nextStdoutStream.blockingWriteAndFlush(Uint8Array.of(67));
  nextStderrStream.blockingWriteAndFlush(Uint8Array.of(68));
  assert.deepEqual(stdoutWrites, [[65], [67]]);
  assert.deepEqual(stderrWrites, [[66], [68]]);
  assert.equal(byModule.get("wasi:cli/terminal-stdout@0.2.11")
    .createSource(request).getTerminalStdout(), undefined);
  assert.equal(byModule.get("wasi:cli/terminal-stderr@0.2.11")
    .createSource(request).getTerminalStderr(), undefined);
  assert.equal(calls.length, 2);
  assert.deepEqual(calls[0], {
    act: "shim",
    config: {
      sandbox: {
        args: ["application", "--value"],
        enableNetwork: false,
        env: { ALPHA: "one" },
        preopens: {},
      },
    },
  });
  assert.equal(Object.isFrozen(calls[0].config), true);
  assert.equal(Object.isFrozen(calls[0].config.sandbox), true);
  assert.equal(Object.isFrozen(calls[0].config.sandbox.env), true);
  assert.equal(Object.isFrozen(calls[0].config.sandbox.preopens), true);
  assert.deepEqual(calls[1], {
    act: "imports",
    options: { asVersion: "0.2.11" },
  });
  assert.equal(Object.isFrozen(calls[1].options), true);
});

test("creates independent shim scopes and preserves caller-owned filesystems", () => {
  const configs = [];
  const sources = sourcesForAllProviders();
  const registrations = createPreview2PlatformProviderRegistrations({
    catalog: platformProviderCatalog,
    createShim(config) {
      configs.push(config);
      return { getImportObject: () => sources };
    },
  });
  const filesystem = Object.freeze({ preopens: {}, types: {} });
  const first = sourceRequest({
    filesystem,
    grants: Object.freeze({
      ...sourceRequest().grants,
      network: "allowAll",
      preopens: Object.freeze([
        Object.freeze({ guestPath: "/data", hostPath: "/host/data", access: "readOnly" }),
      ]),
    }),
  });
  const second = sourceRequest();

  registrations[0].createSource(first);
  registrations[0].createSource(second);

  assert.equal(configs.length, 2);
  assert.strictEqual(configs[0].filesystem, filesystem);
  assert.equal(configs[0].sandbox.enableNetwork, true);
  assert.equal("preopens" in configs[0].sandbox, false);
  assert.equal("filesystem" in configs[1], false);
  assert.deepEqual(configs[1].sandbox.preopens, {});
});

test("requires a caller-owned filesystem for granted preopens", () => {
  let calls = 0;
  const registrations = createPreview2PlatformProviderRegistrations({
    catalog: platformProviderCatalog,
    createShim() {
      calls++;
    },
  });
  const request = sourceRequest({
    grants: Object.freeze({
      ...sourceRequest().grants,
      preopens: Object.freeze([
        Object.freeze({ guestPath: "/data", hostPath: "/host/data", access: "readOnly" }),
      ]),
    }),
  });

  assert.throws(() => registrations[0].createSource(request),
    /caller-owned filesystem/u);
  assert.equal(calls, 0);
});

test("does not retain failed shim or import-object construction", () => {
  const failures = [
    null,
    () => {},
    {},
    { getImportObject: null },
  ];
  for (const shim of failures) {
    const registrations = createPreview2PlatformProviderRegistrations({
      catalog: platformProviderCatalog,
      createShim: () => shim,
    });
    assert.throws(() => registrations[0].createSource(sourceRequest()),
      /invalid shim/u);
  }

  for (const imports of [null, [], () => {}]) {
    let calls = 0;
    const registrations = createPreview2PlatformProviderRegistrations({
      catalog: platformProviderCatalog,
      createShim: () => ({
        getImportObject() {
          calls++;
          return imports;
        },
      }),
    });
    const request = sourceRequest();
    assert.throws(() => registrations[0].createSource(request),
      /invalid import object/u);
    assert.throws(() => registrations[0].createSource(request),
      /invalid import object/u);
    assert.equal(calls, 2);
  }
});

test("rejects missing, array or callable module sources", () => {
  const module = platformProviderCatalog.providers[0].module;
  for (const source of [undefined, null, [], () => {}]) {
    const sources = sourcesForAllProviders();
    sources[module] = source;
    const registrations = createPreview2PlatformProviderRegistrations({
      catalog: platformProviderCatalog,
      createShim: () => ({
        getImportObject: () => sources,
      }),
    });
    assert.throws(() => registrations[0].createSource(sourceRequest()),
      /does not provide module/u);
  }
});

test("rejects missing or malformed output resource types", () => {
  for (const source of [undefined, null, [], () => {}]) {
    const sources = sourcesForAllProviders();
    sources["wasi:io/streams@0.2.11"] = source;
    const registrations = createPreview2PlatformProviderRegistrations({
      catalog: platformProviderCatalog,
      createShim: () => ({ getImportObject: () => sources }),
    });
    assert.throws(() => registrations[0].createSource(sourceRequest()), /does not provide module/);
  }
  const sourceAccessor = sourcesForAllProviders();
  Object.defineProperty(sourceAccessor, "wasi:io/streams@0.2.11", {
    enumerable: true,
    get: () => Object.freeze({ OutputStream }),
  });
  const accessorRegistrations = createPreview2PlatformProviderRegistrations({
    catalog: platformProviderCatalog,
    createShim: () => ({ getImportObject: () => sourceAccessor }),
  });
  assert.throws(
    () => accessorRegistrations[0].createSource(sourceRequest()),
    /does not provide module/);

  for (const [module, name, value] of [
    ["wasi:io/streams@0.2.11", "OutputStream", undefined],
    ["wasi:io/streams@0.2.11", "OutputStream", null],
    ["wasi:io/poll@0.2.11", "Pollable", undefined],
    ["wasi:io/poll@0.2.11", "Pollable", {}],
  ]) {
    const sources = sourcesForAllProviders();
    sources[module] = Object.freeze({ [name]: value });
    const registrations = createPreview2PlatformProviderRegistrations({
      catalog: platformProviderCatalog,
      createShim: () => ({ getImportObject: () => sources }),
    });
    assert.throws(() => registrations[0].createSource(sourceRequest()), /does not provide resource/);
  }
  const sources = sourcesForAllProviders();
  sources["wasi:io/streams@0.2.11"] = Object.freeze(Object.defineProperty(
    {}, "OutputStream", { enumerable: true, get: () => class {} }));
  const registrations = createPreview2PlatformProviderRegistrations({
    catalog: platformProviderCatalog,
    createShim: () => ({ getImportObject: () => sources }),
  });
  assert.throws(() => registrations[0].createSource(sourceRequest()), /does not provide resource/);
});

test("validates exact options and compatible frozen catalog metadata", () => {
  const valid = {
    catalog: platformProviderCatalog,
    createShim() {},
  };
  for (const value of invalidObjects(valid)) {
    assert.throws(() => createPreview2PlatformProviderRegistrations(value.value),
      value.shape ? /options shape/u : /options is invalid/u);
  }
  assert.throws(() => createPreview2PlatformProviderRegistrations({
    ...valid,
    createShim: null,
  }), /creator is required/u);

  const invalidCatalogs = [
    null,
    [],
    { ...platformProviderCatalog },
    Object.freeze({ ...platformProviderCatalog, schema: 2 }),
    Object.freeze({
      ...platformProviderCatalog,
      world: "netwasm:hosting-platform/preview2@1.0.1",
    }),
    Object.freeze({ ...platformProviderCatalog, witSha256: "bad" }),
    Object.freeze({
      ...platformProviderCatalog,
      shim: Object.freeze({ ...platformProviderCatalog.shim, version: "0.24.0" }),
    }),
    Object.freeze({ ...platformProviderCatalog, providers: [] }),
    Object.freeze({ ...platformProviderCatalog, providers: Object.freeze([]) }),
  ];
  for (const catalog of invalidCatalogs) {
    assert.throws(() => createPreview2PlatformProviderRegistrations({
      ...valid,
      catalog,
    }), TypeError);
  }
});

test("validates every provider and exact source request shape", () => {
  const validProvider = platformProviderCatalog.providers[0];
  for (const provider of [
    null,
    { ...validProvider },
    Object.freeze({ ...validProvider, module: "" }),
    Object.freeze({ ...validProvider, functions: [] }),
    Object.freeze({ ...validProvider, functions: Object.freeze([]) }),
  ]) {
    const catalog = Object.freeze({
      ...platformProviderCatalog,
      providers: Object.freeze([provider]),
    });
    assert.throws(() => createPreview2PlatformProviderRegistrations({
      catalog,
      createShim() {},
    }), TypeError);
  }

  const registrations = createPreview2PlatformProviderRegistrations({
    catalog: platformProviderCatalog,
    createShim() {},
  });
  const validRequest = sourceRequest();
  for (const value of invalidObjects(validRequest)) {
    assert.throws(() => registrations[0].createSource(value.value),
      value.shape ? /request shape/u : /request is invalid/u);
  }
  for (const request of [
    { ...validRequest },
    Object.freeze({ ...validRequest, arguments: [] }),
    Object.freeze({ ...validRequest, environment: [] }),
    Object.freeze({ ...validRequest, grants: { ...validRequest.grants } }),
    Object.freeze({
      ...validRequest,
      grants: Object.freeze({ ...validRequest.grants, preopens: null }),
    }),
    Object.freeze({ ...validRequest, filesystem: [] }),
  ]) {
    assert.throws(() => registrations[0].createSource(request), TypeError);
  }
});

function invalidObjects(valid) {
  const values = [
    { value: null, shape: false },
    { value: [], shape: false },
    { value: () => {}, shape: false },
    { value: { ...valid, extra: true }, shape: true },
  ];
  for (const key of Object.keys(valid)) {
    const missing = { ...valid };
    delete missing[key];
    values.push({ value: missing, shape: true });
  }
  return values;
}
