import assert from "node:assert/strict";
import test from "node:test";

import { createWasiPlatform, isWasiExit } from "./wasi-platform.mjs";

const quiet = Object.freeze({ write() {} });

function request(overrides = {}) {
  return Object.freeze({
    arguments: Object.freeze(["app", "--flag"]),
    environment: Object.freeze([Object.freeze({ name: "MODE", value: "test" })]),
    filesystem: null,
    grants: Object.freeze({ network: "denyAll" }),
    signal: null,
    stderr: quiet,
    stdin: null,
    stdout: quiet,
    ...overrides,
  });
}

function stream(chunks) {
  return { read: async () => (chunks.length === 0 ? { done: true } : { done: false, value: chunks.shift() }) };
}

test("supplies the execution's command line, environment and standard streams", async () => {
  const written = { stdout: [], stderr: [] };
  const createImports = createWasiPlatform(Object.freeze({ initialDirectory: null, network: null }));
  assert.equal(Object.isFrozen(createImports), true);
  const importSet = createImports(request({
    stdout: Object.freeze({ write: bytes => written.stdout.push(...bytes) }),
    stderr: Object.freeze({ write: bytes => written.stderr.push(...bytes) }),
  }));
  const { imports } = importSet;
  assert.equal(Object.isFrozen(importSet), true);
  assert.equal(Object.isFrozen(imports), true);
  const environment = imports["wasi:cli/environment@0.3.2"];
  assert.deepEqual(environment.getArguments(), ["app", "--flag"]);
  assert.deepEqual(environment.getEnvironment(), [["MODE", "test"]]);
  assert.equal(environment.getInitialCwd(), undefined);

  assert.deepEqual(await imports["wasi:cli/stdout@0.3.2"].writeViaStream(stream([Uint8Array.of(1, 2)])),
    { tag: "ok", val: undefined });
  assert.deepEqual(await imports["wasi:cli/stderr@0.3.2"].writeViaStream(stream([[3]])),
    { tag: "ok", val: undefined });
  assert.deepEqual(written, { stdout: [1, 2], stderr: [3] });

  const [stdin, stdinResult] = imports["wasi:cli/stdin@0.3.2"].readViaStream();
  assert.deepEqual([...stdin], []);
  assert.deepEqual(await stdinResult, { tag: "ok", val: undefined });
  assert.equal(imports["wasi:cli/terminal-stdin@0.3.2"].getTerminalStdin(), undefined);
  assert.equal(imports["wasi:cli/terminal-stdout@0.3.2"].getTerminalStdout(), undefined);
  assert.equal(imports["wasi:cli/terminal-stderr@0.3.2"].getTerminalStderr(), undefined);
  assert.equal(typeof imports["wasi:cli/terminal-input@0.3.2"].TerminalInput, "function");
  assert.equal(typeof imports["wasi:cli/terminal-output@0.3.2"].TerminalOutput, "function");
  assert.equal(imports["wasi:filesystem/types@0.3.2"], undefined);
  assert.equal(imports["wasi:http/client@0.3.2"], undefined);
  await importSet.release();
  await importSet.release();
});

test("unwinds the guest with the status of a wasi:cli/exit call", () => {
  const { imports } = createWasiPlatform(Object.freeze({ initialDirectory: null, network: null }))(request());
  const exit = imports["wasi:cli/exit@0.3.2"];
  for (const [call, code] of [
    [() => exit.exit({ tag: "ok", val: undefined }), 0],
    [() => exit.exit({ tag: "err", val: undefined }), 1],
    [() => exit.exit(undefined), 0],
    [() => exit.exitWithCode(255), 255],
  ]) {
    assert.throws(call, value => isWasiExit(value) && value.code === code && Object.isFrozen(value));
  }
  for (const code of [-1, 256, 1.5, "1"]) {
    assert.throws(() => exit.exitWithCode(code), TypeError);
  }
  for (const value of [null, undefined, 1, {}, new Error("exit")]) {
    assert.equal(isWasiExit(value), false);
  }
});

test("reads the monotonic and system clocks and waits on the monotonic clock", async () => {
  const { imports, release } = createWasiPlatform(Object.freeze({ initialDirectory: null, network: null }))(request());
  const monotonic = imports["wasi:clocks/monotonic-clock@0.3.2"];
  const system = imports["wasi:clocks/system-clock@0.3.2"];
  const before = monotonic.now();
  assert.equal(typeof before, "bigint");
  assert.equal(monotonic.getResolution(), 1000n);
  await monotonic.waitFor(2_000_000n);
  assert.ok(monotonic.now() - before >= 1_000_000n);
  await monotonic.waitFor(0n);
  await monotonic.waitFor(0xffffffffffffffffn);
  await monotonic.waitUntil(monotonic.now() + 1_000_000n);
  await monotonic.waitUntil(0n);
  const now = system.now();
  assert.equal(typeof now.seconds, "bigint");
  assert.ok(now.nanoseconds >= 0 && now.nanoseconds < 1_000_000_000);
  assert.ok(Math.abs(Number(now.seconds) - Date.now() / 1000) < 5);
  assert.equal(system.getResolution(), 1000n);
  await release();
});

test("long waits run in steps the JavaScript timer accepts and stop at release", async () => {
  const delays = [];
  const cleared = [];
  const originalSet = globalThis.setTimeout;
  const originalClear = globalThis.clearTimeout;
  const pending = [];
  globalThis.setTimeout = (callback, delay) => {
    delays.push(delay);
    const timer = { callback };
    if (delay === 0) originalSet(callback, 0);
    else pending.push(timer);
    return timer;
  };
  globalThis.clearTimeout = timer => cleared.push(timer);
  try {
    const { imports, release } = createWasiPlatform(Object.freeze({ initialDirectory: null, network: null }))(request());
    let settled = false;
    imports["wasi:clocks/monotonic-clock@0.3.2"].waitFor(3_000_000_000_000_000n).then(() => { settled = true; });
    assert.deepEqual(delays, [0x7fffffff]);
    pending.shift().callback();
    assert.equal(delays.length, 2);
    assert.equal(settled, false);
    await release();
    assert.equal(cleared.length, 1);
    let late = false;
    imports["wasi:clocks/monotonic-clock@0.3.2"].waitFor(1n).then(() => { late = true; });
    await new Promise(resolve => originalSet(resolve, 5));
    assert.equal(late, false);
  } finally {
    globalThis.setTimeout = originalSet;
    globalThis.clearTimeout = originalClear;
  }
});

test("draws secure and insecure random values of any requested length", () => {
  const { imports } = createWasiPlatform(Object.freeze({ initialDirectory: null, network: null }))(request());
  const random = imports["wasi:random/random@0.3.2"];
  const insecure = imports["wasi:random/insecure@0.3.2"];
  assert.equal(random.getRandomBytes(0n).length, 0);
  assert.equal(random.getRandomBytes(70_000n).length, 70_000);
  assert.equal(insecure.getInsecureRandomBytes(3n).length, 3);
  assert.equal(typeof random.getRandomU64(), "bigint");
  assert.equal(typeof insecure.getInsecureRandomU64(), "bigint");
  const seed = imports["wasi:random/insecure-seed@0.3.2"].getInsecureSeed();
  assert.equal(seed.length, 2);
  assert.throws(() => random.getRandomBytes(0x80000000n), RangeError);
});

test("adds the filesystem scope and granted network modules", () => {
  const filesystem = Object.freeze({ types: Object.freeze({}), preopens: Object.freeze({}) });
  const client = Object.freeze({ send() {} });
  const network = Object.freeze({ modules: Object.freeze({ "wasi:http/client@0.3.2": client }) });
  const createImports = createWasiPlatform(Object.freeze({ initialDirectory: "/work/project", network }));
  const granted = createImports(request({ filesystem, grants: Object.freeze({ network: "allowAll" }) })).imports;
  assert.equal(granted["wasi:filesystem/types@0.3.2"], filesystem.types);
  assert.equal(granted["wasi:filesystem/preopens@0.3.2"], filesystem.preopens);
  assert.equal(granted["wasi:http/client@0.3.2"], client);
  assert.equal(granted["wasi:cli/environment@0.3.2"].getInitialCwd(), "/work/project");
  assert.equal(createImports(request()).imports["wasi:http/client@0.3.2"], undefined);
});

// Standard input is the execution's input source, opened when the guest first reads it; every
// read-via-stream shares it, and release stops a read the guest left waiting.
test("reads standard input from the execution's source", async () => {
  const opened = [];
  const pending = new Promise(() => {});
  const chunks = [{ done: false, value: Uint8Array.of(1, 2) }, { done: false, value: "x" }, pending];
  const iterator = { next: () => chunks.shift(), return() { opened.push("return"); return { done: true }; } };
  const stdin = Object.freeze({ read() { opened.push("read"); return iterator; } });
  const { imports, release } = createWasiPlatform(Object.freeze({ initialDirectory: null, network: null }))(request({ stdin }));
  const readViaStream = imports["wasi:cli/stdin@0.3.2"].readViaStream;
  assert.deepEqual(opened, []);

  const [first, firstResult] = readViaStream();
  const [second] = readViaStream();
  assert.deepEqual((await first.next()).value, Uint8Array.of(1, 2));
  assert.deepEqual((await second.next()).value, Uint8Array.of(0x78));
  assert.deepEqual(opened, ["read"]);
  const waiting = second.next();
  await release();
  assert.deepEqual(opened, ["read", "return"]);
  await first.return();
  assert.deepEqual(await firstResult, { tag: "ok", val: undefined });
  void waiting;
});

test("gives an execution without standard input an empty stream", async () => {
  const [stream, result] = createWasiPlatform(Object.freeze({ initialDirectory: null, network: null }))(request())
    .imports["wasi:cli/stdin@0.3.2"].readViaStream();
  assert.deepEqual(stream, []);
  assert.deepEqual(await result, { tag: "ok", val: undefined });
  await createWasiPlatform(Object.freeze({ initialDirectory: null, network: null }))(request()).release();
});

test("release lets output already in flight reach its sink", async () => {
  const written = [];
  const { imports, release } = createWasiPlatform(Object.freeze({ initialDirectory: null, network: null }))(request({
    stdout: Object.freeze({ write: bytes => written.push(...bytes) }),
  }));
  let deliver;
  const pending = new Promise(resolve => { deliver = resolve; });
  const chunks = [pending, { done: true }];
  imports["wasi:cli/stdout@0.3.2"].writeViaStream({ read: () => chunks.shift() });
  const releasing = release();
  deliver({ done: false, value: [7] });
  await releasing;
  assert.deepEqual(written, [7]);
});

test("release drops guest streams the guest left open so no host read stays pending", async () => {
  const dropped = [];
  const pending = () => new Promise(() => {});
  const { imports, release } = createWasiPlatform(Object.freeze({ initialDirectory: null, network: null }))(request());
  const write = imports["wasi:cli/stdout@0.3.2"].writeViaStream;
  write({ read: pending, [Symbol.dispose]() { dropped.push("dispose"); } });
  write({ read: pending, [Symbol.for("dispose")]() { dropped.push("registered"); } });
  write({ read: pending, async return() { dropped.push("return"); } });
  write({ read: pending, [Symbol.dispose]() { throw new Error("already gone"); } });
  write({ read: pending });
  await release();
  assert.deepEqual(dropped, ["dispose", "registered", "return"]);
});

test("rejects malformed services and requests", () => {
  for (const network of [{}, { modules: null }, { modules: {} }, { modules: Object.freeze({}) },
    { modules: Object.freeze({ "wasi:cli/stdout@0.3.2": {} }) },
    { modules: Object.freeze({ "wasi:http/client@0.3.2": null }) }]) {
    assert.throws(() => createWasiPlatform({ initialDirectory: null, network }), TypeError);
  }
  for (const services of [null, {}, { initialDirectory: null, network: null, extra: 1 }, { network: null }]) {
    assert.throws(() => createWasiPlatform(services), TypeError);
  }
  for (const initialDirectory of [1, "relative", "/a\0b"]) {
    assert.throws(() => createWasiPlatform({ initialDirectory, network: null }), /initial directory/u);
  }
  const createImports = createWasiPlatform(Object.freeze({ initialDirectory: null, network: null }));
  for (const overrides of [
    { stdin: {} }, { stdin: Object.freeze({ read: 1 }) },
    { arguments: null }, { arguments: [1] }, { environment: null }, { environment: [null] },
    { environment: [{ name: "A", value: 1 }] }, { grants: null }, { stdout: {} },
    { stderr: Object.freeze({ write: 1 }) }, { filesystem: 1 },
    { filesystem: { types: null, preopens: {} } }, { filesystem: { types: {}, preopens: null } },
  ]) {
    assert.throws(() => createImports({ ...request(), ...overrides }), TypeError);
  }
  assert.throws(() => createImports({ ...request(), extra: true }), TypeError);
});
