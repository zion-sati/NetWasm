import assert from "node:assert/strict";
import test from "node:test";
import { createHash } from "node:crypto";
import { createNetWasmWasiHost } from "./wasi-host.mjs";

test("WASI host copies immediately and loads diagnostic artifacts lazily", async () => {
  const memory = new WebAssembly.Memory({ initial: 1 });
  const units = new Uint16Array(memory.buffer, 16, 2);
  units.set([0x006f, 0x006b]);
  const immediate = [];
  const enriched = [];
  let loads = 0;
  const host = createNetWasmWasiHost({
    wasiImports: { "wasi:cli/exit@0.2.11": { exit() {} } },
    stringDataOffset: 0,
    diagnosticArtifacts: async () => {
      loads++;
      throw new Error("map unavailable");
    },
    managedExceptionReporting: {
      reportImmediate: event => immediate.push(event),
      reportEnriched: event => enriched.push(event),
    },
  });

  assert.equal(loads, 0);
  assert.equal(host.imports["wasi:cli/exit@0.2.11"].exit instanceof Function, true);
  host.bindInstance({ exports: { memory } });
  host.imports["netwasm.host.v1"].report_terminal_exception_v1(7, 16, 2);
  assert.equal(loads, 1);
  assert.deepEqual(immediate, [{ eventId: 1, typeId: 7, message: "ok", messageTruncated: false }]);
  memory.grow(1);
  await new Promise(resolve => setTimeout(resolve, 0));
  assert.equal(enriched.length, 1);
  assert.equal(enriched[0].eventId, 1);
  assert.match(enriched[0].artifactError, /map unavailable/);
});

test("WASI host rejects execution binding without exported memory", () => {
  const host = createNetWasmWasiHost({
    wasiImports: {},
    stringDataOffset: 0,
  });

  assert.throws(() => host.bindInstance({ exports: {} }), /must export/);
});

for (const prefix of ["", "cm32p2|", "cm64p2|"]) {
  test(`WASI host preserves explicitly supplied Preview 2 interface ${prefix}`, () => {
    const namespace = `${prefix}wasi:cli/environment@0.2`;
    const members = { "get-environment": () => [] };
    const application = { run: () => 42 };
    const host = createNetWasmWasiHost({
      wasiImports: { [namespace]: members },
      additionalImports: { "application:test": application },
      stringDataOffset: 0,
    });
    assert.equal(host.imports[namespace], members);
    assert.equal(host.imports["application:test"], application);
    assert.equal(Object.isFrozen(host.imports), true);
  });
}

for (const namespace of ["wasi_snapshot_preview1", "wasi_unstable"]) {
  for (const location of ["wasiImports", "additionalImports"]) {
    test(`WASI host rejects legacy namespace in ${location}: ${namespace}`, () => {
      assert.throws(() => createNetWasmWasiHost({
        wasiImports: {},
        stringDataOffset: 0,
        [location]: { [namespace]: {} },
      }), /Preview [12]/);
    });
  }
}

test("WASI host rejects flat legacy imports", () => {
  assert.throws(() => createNetWasmWasiHost({
    wasiImports: { proc_exit() {} },
    stringDataOffset: 0,
  }), /namespaced WASI Preview 2/);
});

for (const members of [null, 7, () => {}]) {
  test(`WASI host rejects invalid interface members: ${typeof members}`, () => {
    assert.throws(() => createNetWasmWasiHost({
      wasiImports: { "wasi:cli/environment@0.2": members },
      stringDataOffset: 0,
    }), /members must be an object/);
  });
}

for (const namespace of ["netwasm.host.v1", "wasi:cli/environment@0.2"]) {
  test(`WASI host rejects overwritten namespace: ${namespace}`, () => {
    assert.throws(() => createNetWasmWasiHost({
      wasiImports: { "wasi:cli/environment@0.2": {} },
      additionalImports: { [namespace]: {} },
      stringDataOffset: 0,
    }), /must not overwrite/);
  });
}

for (const property of ["wasiImports", "additionalImports"]) {
  for (const value of [null, 7, undefined]) {
    if (property === "additionalImports" && value === undefined) continue;
    test(`WASI host rejects invalid ${property}: ${typeof value}`, () => {
      assert.throws(() => createNetWasmWasiHost({
        wasiImports: {},
        stringDataOffset: 0,
        [property]: value,
      }), new RegExp(`${property} must be an object`));
    });
  }
}

for (const artifactsAvailable of [false, true]) {
  test(`WASI host default reporting with artifacts available: ${artifactsAvailable}`, async t => {
    const memory = new WebAssembly.Memory({ initial: 1 });
    new Uint16Array(memory.buffer, 16, 2).set([0x006f, 0x006b]);
    const mapBytes = new TextEncoder().encode(JSON.stringify({
      schemaVersion: 2,
      buildId: "unit-report",
      entries: [{
        typeId: 7,
        displayName: "ExampleException",
        canonicalIdentity: "ExampleException, Unit",
        assemblyIdentity: "Unit",
      }],
    }));
    const wasmBytes = new TextEncoder().encode("unit artifact bytes");
    const artifacts = {
      manifest: {
        schemaVersion: 1,
        buildId: "unit-report",
        exceptionTypeMapSha256: createHash("sha256").update(mapBytes).digest("hex"),
        wasmSha256: createHash("sha256").update(wasmBytes).digest("hex"),
      },
      mapBytes,
      wasmBytes,
    };
    const messages = [];
    let complete;
    const reported = new Promise(resolve => { complete = resolve; });
    t.mock.method(console, "error", message => {
      messages.push(message);
      if (messages.length === 2) complete();
    });
    const host = createNetWasmWasiHost({
      wasiImports: {},
      stringDataOffset: 0,
      diagnosticArtifacts: artifactsAvailable ? artifacts : null,
      managedExceptionReporting: { maximumMessageLength: 1 },
    });
    const instance = { exports: { memory } };
    assert.equal(host.bindInstance(instance), instance);
    host.imports["netwasm.host.v1"].report_terminal_exception_v1(7, 16, 2);
    assert.deepEqual(host.consumeTerminalEvent(), {
      eventId: 1, typeId: 7, message: "o", messageTruncated: true,
    });
    assert.equal(host.consumeTerminalEvent(), undefined);
    await reported;
    assert.deepEqual(messages, [
      "[netwasm:1] managed exception typeId=7: o",
      `[netwasm:1] resolved exception type: ${artifactsAvailable ? "ExampleException" : "<exception type #7 unavailable>"}`,
    ]);
  });
}
