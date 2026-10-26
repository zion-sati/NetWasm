import assert from "node:assert/strict";
import test from "node:test";

import * as browser from "@bytecodealliance/preview2-shim";
import { WASIShim } from "@bytecodealliance/preview2-shim/instantiation";

import { createSelectedPreview2ShimFactory } from "./preview2-selected-shim.mjs";

const allFamilies = Object.freeze({
  cli: browser.cli,
  clocks: browser.clocks,
  filesystem: browser.filesystem,
  http: browser.http,
  io: browser.io,
  random: browser.random,
  sockets: browser.sockets,
});

function sandbox(extra = {}) {
  return Object.freeze({
    args: Object.freeze(["first", "second"]),
    enableNetwork: false,
    env: Object.freeze({ NAME: "value" }),
    ...extra,
  });
}

// Both configuration shapes NetWasm's provider registrations create: a caller
// filesystem, or sandbox preopens without one.
const configurations = [
  ["caller filesystem", () => Object.freeze({
    filesystem: browser.filesystem.createFilesystem({ preopens: Object.freeze({}) }),
    sandbox: sandbox(),
  })],
  ["sandbox preopens", () => Object.freeze({ sandbox: sandbox({ preopens: Object.freeze({}) }) })],
];

for (const [label, configure] of configurations) {
  test(`with every family the import object matches WASIShim (${label})`, () => {
    const config = configure();
    const expected = new WASIShim(config).getImportObject({ asVersion: "0.2.11" });
    const actual = createSelectedPreview2ShimFactory(allFamilies)(config)
      .getImportObject({ asVersion: "0.2.11" });

    assert.deepEqual(Object.keys(actual).sort(), Object.keys(expected).sort());
    for (const name of Object.keys(expected)) {
      if (name === "wasi:cli/environment@0.2.11") continue;
      if (name.startsWith("wasi:filesystem/") || name.startsWith("wasi:sockets/")) {
        // Each shim creates its own isolated filesystem and socket state.
        assert.deepEqual(Object.keys(actual[name]).sort(), Object.keys(expected[name]).sort(), name);
        continue;
      }
      assert.equal(actual[name], expected[name], name);
    }
    const environment = actual["wasi:cli/environment@0.2.11"];
    const expectedEnvironment = expected["wasi:cli/environment@0.2.11"];
    assert.deepEqual(environment.getArguments(), expectedEnvironment.getArguments());
    assert.deepEqual(environment.getEnvironment(), expectedEnvironment.getEnvironment());
  });
}

test("an application that links only cli, io and random gets no other family", () => {
  const shim = createSelectedPreview2ShimFactory({
    cli: browser.cli,
    io: browser.io,
    random: browser.random,
  })(Object.freeze({ sandbox: sandbox({ preopens: Object.freeze({}) }) }));

  const names = Object.keys(shim.getImportObject({ asVersion: "0.2.11" }));

  assert.ok(names.includes("wasi:random/random@0.2.11"));
  assert.ok(names.includes("wasi:io/streams@0.2.11"));
  assert.ok(!names.some(name => /^wasi:(filesystem|sockets|http|clocks)\//u.test(name)), names.join());
});

test("selected families must include cli and io and name only known families", () => {
  assert.throws(() => createSelectedPreview2ShimFactory({ cli: browser.cli }), TypeError);
  assert.throws(() => createSelectedPreview2ShimFactory({ io: browser.io }), TypeError);
  assert.throws(() => createSelectedPreview2ShimFactory({
    cli: browser.cli, io: browser.io, gpu: {},
  }), TypeError);
});

test("a caller filesystem and sandbox preopens together are rejected", () => {
  const create = createSelectedPreview2ShimFactory(allFamilies);
  assert.throws(() => create(Object.freeze({
    filesystem: browser.filesystem.createFilesystem({ preopens: Object.freeze({}) }),
    sandbox: sandbox({ preopens: Object.freeze({}) }),
  })), TypeError);
});

// Small stand-in families make each branch of the composition observable.
function fakeFamilies(extra = {}) {
  const environment = {
    getArguments: () => ["base-argument"],
    getEnvironment: () => [["BASE", "1"]],
    initialCwd: () => "/base",
  };
  return {
    cli: { environment, exit: {}, stderr: {}, stdin: {}, stdout: {} },
    io: { error: {}, poll: {}, streams: {} },
    ...extra,
  };
}

test("names are unversioned when no version is requested", () => {
  const shim = createSelectedPreview2ShimFactory(fakeFamilies())(Object.freeze({ sandbox: sandbox() }));
  const names = Object.keys(shim.getImportObject());
  assert.ok(names.includes("wasi:cli/environment"));
  assert.ok(names.includes("wasi:io/streams"));
});

test("a sockets family without a factory is used as it is", () => {
  const sockets = { instanceNetwork: {}, network: {}, tcp: {} };
  const imports = createSelectedPreview2ShimFactory(fakeFamilies({ sockets }))(
    Object.freeze({ sandbox: sandbox() })).getImportObject({ asVersion: "0.2.11" });
  assert.equal(imports["wasi:sockets/tcp@0.2.11"], sockets.tcp);
});

test("the isolated environment falls back to the CLI family without sandbox values", () => {
  const environment = createSelectedPreview2ShimFactory(fakeFamilies())(
    Object.freeze({ sandbox: Object.freeze({ enableNetwork: false }) }))
    .getImportObject({ asVersion: "0.2.11" })["wasi:cli/environment@0.2.11"];
  assert.deepEqual(environment.getArguments(), ["base-argument"]);
  assert.deepEqual(environment.getEnvironment(), [["BASE", "1"]]);
  assert.equal(environment.initialCwd(), "/base");
});

test("invalid families and configurations are rejected", () => {
  for (const families of [null, 1, [], { ...fakeFamilies(), random: null }]) {
    assert.throws(() => createSelectedPreview2ShimFactory(families), TypeError);
  }
  const create = createSelectedPreview2ShimFactory(fakeFamilies());
  for (const config of [
    null,
    Object.freeze({ sandbox: null }),
    Object.freeze({ sandbox: sandbox(), extra: true }),
    Object.freeze({ sandbox: Object.freeze({ unknown: true }) }),
  ]) {
    assert.throws(() => create(config), TypeError);
  }
});
