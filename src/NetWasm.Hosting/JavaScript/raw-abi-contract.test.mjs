import assert from "node:assert/strict";
import test from "node:test";
import {
  commandExecutionContract as commandComponentContract,
  processExecutionContract as processComponentContract,
} from "./execution-contracts.mjs";
import { buildRawAbiPlan } from "./raw-abi-plan-builder.mjs";
import { bindRawExecutionInstance } from "./raw-execution-instance-binder.mjs";
import { composeRawImports } from "./raw-import-composer.mjs";

const functionImport = (module = "wasi:cli/stdout@0.2.11", name = "get-stdout") =>
  ({ module, name, kind: "function" });
const functionExport = name => ({ name, kind: "function" });
const entryPoint = (completionShape = "synchronous", returnShape = "exitCode",
  parameterShape = "none") => ({ parameterShape, returnShape, completionShape });
const commandAbi = (overrides = {}) => ({
  target: "wasm32",
  entryPoint: entryPoint(),
  imports: [functionImport()],
  exports: [functionExport("run")],
  ...overrides,
});
const processAbi = ({ target = "wasm32", returnShape = "exitCode" } = {}) => {
  const prefix = target === "wasm64" ? "cm64p2" : "cm32p2";
  return {
    target,
    entryPoint: entryPoint("asynchronous", returnShape, "stringArray"),
    imports: [
      functionImport(),
      functionImport(`${prefix}|netwasm:runtime/reactor-host@1`, "watch"),
      functionImport(`${prefix}|netwasm:runtime/reactor-host@1`, "cancel"),
    ],
    exports: [
      functionExport("run"),
      functionExport("netwasm.process.status"),
      ...(returnShape === "exitCode" ? [functionExport("netwasm.process.result")] : []),
      functionExport("netwasm.process.complete"),
      functionExport(`${prefix}|netwasm:runtime/reactor-guest@1|wake`),
    ],
  };
};
const providers = () => ({
  "wasi:cli/stdout@0.2.11": {
    "get-stdout"() { return "stdout"; },
    extra() {},
  },
  unused: { value: 1 },
});
const reactorHost = () => ({ watch() {}, cancel() {}, extra() {} });

function prepareRawContract({ contractKey, abi, providers: physicalProviders, reactorHost: host }) {
  const plan = buildRawAbiPlan({ contractKey, abi });
  const canonicalImports = Object.create(null);
  if (host !== undefined && host !== null) {
    canonicalImports[plan.reactorHostModule] = Object.freeze({
      watch: host.watch,
      cancel: host.cancel,
    });
  }
  const canonicalBinding = Object.freeze({
    imports: Object.freeze(canonicalImports),
    target: plan.target,
  });
  const imports = composeRawImports({
    canonicalBinding,
    physicalProviders,
    plan,
  });
  return Object.freeze({
    contractKey: plan.contractKey,
    target: plan.target,
    imports,
    bind(instance) { return bindRawExecutionInstance({ instance, plan }); },
  });
}

test("binds exact wasm32 command imports and preserves signed exit", () => {
  const plan = prepareRawContract({
    contractKey: commandComponentContract,
    abi: commandAbi(),
    providers: providers(),
  });
  assert.equal(plan.contractKey, commandComponentContract);
  assert.equal(plan.target, "wasm32");
  assert.equal(Object.getPrototypeOf(plan.imports), null);
  assert.deepEqual(Object.keys(plan.imports), ["wasi:cli/stdout@0.2.11"]);
  assert.deepEqual(Object.keys(plan.imports["wasi:cli/stdout@0.2.11"]), ["get-stdout"]);
  assert.equal(Object.isFrozen(plan), true);
  assert.equal(Object.isFrozen(plan.imports), true);
  assert.equal(Object.isFrozen(plan.imports["wasi:cli/stdout@0.2.11"]), true);
  const calls = [];
  const exports = { marker: 37, run() { calls.push(this.marker); return -17; } };
  const binding = plan.bind({ exports });
  assert.equal(binding.command.run(), -17);
  assert.deepEqual(calls, [37]);
});

test("snapshots entry-point metadata before binding an instance", () => {
  const abi = commandAbi();
  const plan = prepareRawContract({
    contractKey: commandComponentContract,
    abi,
    providers: providers(),
  });
  abi.entryPoint.returnShape = "void";
  const binding = plan.bind({ exports: { run: () => -31 } });
  assert.equal(binding.command.run(), -31);
});

test("snapshots final import and export descriptors into a passive plan", () => {
  const abi = commandAbi();
  const plan = buildRawAbiPlan({ contractKey: commandComponentContract, abi });
  abi.imports[0].name = "changed";
  abi.imports.push(functionImport("other", "value"));
  abi.exports[0].name = "changed";
  assert.deepEqual(plan.imports, [functionImport()]);
  assert.deepEqual(plan.exports, [functionExport("run")]);
  assert.equal(Object.isFrozen(plan), true);
  assert.equal(Object.isFrozen(plan.imports), true);
  assert.equal(Object.isFrozen(plan.imports[0]), true);
  assert.equal(Object.isFrozen(plan.exports), true);
  assert.equal(Object.isFrozen(plan.exports[0]), true);
  assert.equal(Object.hasOwn(plan, "bind"), false);
});

test("adapts void and target-width string-array command entry points", () => {
  for (const [target, zero] of [["wasm32", 0], ["wasm64", 0n]]) {
    const calls = [];
    const plan = prepareRawContract({
      contractKey: commandComponentContract,
      abi: commandAbi({
        target,
        entryPoint: entryPoint("synchronous", "void", "stringArray"),
      }),
      providers: providers(),
    });
    const binding = plan.bind({ exports: { run(value) { calls.push(value); } } });
    assert.equal(binding.command.run(), 0);
    assert.deepEqual(calls, [zero]);
  }
});

test("binds exact wasm32 and wasm64 process boundaries", () => {
  for (const target of ["wasm32", "wasm64"]) {
    const prefix = target === "wasm64" ? "cm64p2" : "cm32p2";
    const calls = [];
    const plan = prepareRawContract({
      contractKey: processComponentContract,
      abi: processAbi({ target }),
      providers: providers(),
      reactorHost: reactorHost(),
    });
    assert.deepEqual(Object.keys(plan.imports[`${prefix}|netwasm:runtime/reactor-host@1`]),
      ["watch", "cancel"]);
    const exports = {
      marker: target,
      run(value) { calls.push([this.marker, "start", value]); return 7; },
      "netwasm.process.status"(handle) { calls.push([this.marker, "status", handle]); return 1; },
      "netwasm.process.result"(handle) { calls.push([this.marker, "result", handle]); return -23; },
      "netwasm.process.complete"(handle) { calls.push([this.marker, "complete", handle]); },
      [`${prefix}|netwasm:runtime/reactor-guest@1|wake`](token) {
        calls.push([this.marker, "wake", token]);
      },
    };
    const binding = plan.bind({ exports });
    assert.equal(binding.process.start(), 7);
    assert.equal(binding.process.status(7), 1);
    assert.equal(binding.process.exitCode(7), -23);
    binding.process.complete(7);
    binding.reactorGuest.wake(11);
    assert.deepEqual(calls.map(call => call[1]), ["start", "status", "result", "complete", "wake"]);
    assert.equal(calls[0][2], target === "wasm64" ? 0n : 0);
    assert.equal(calls.every(call => call[0] === target), true);
  }
});

test("normalizes a void process result without requiring a result export", () => {
  const plan = prepareRawContract({
    contractKey: processComponentContract,
    abi: processAbi({ returnShape: "void" }),
    providers: providers(),
    reactorHost: reactorHost(),
  });
  const binding = plan.bind({ exports: {
    run: () => 3,
    "netwasm.process.status": () => 1,
    "netwasm.process.complete": () => {},
    "cm32p2|netwasm:runtime/reactor-guest@1|wake": () => {},
  } });
  assert.equal(binding.process.exitCode(3), 0);
});

test("rejects malformed request, ABI and entry-point shapes", () => {
  const valid = { contractKey: commandComponentContract, abi: commandAbi(), providers: providers() };
  for (const request of [null, [], 1]) {
    assert.throws(() => buildRawAbiPlan(request), /request/);
  }
  assert.throws(() => prepareRawContract({ ...valid, contractKey: "other@1.0.0" }), /unsupported/);
  for (const abi of [null, [], {}, { ...commandAbi(), extra: true },
    Object.defineProperty({ ...commandAbi() }, "target", { get: () => "wasm32", enumerable: true }),
    { ...commandAbi(), [Symbol("bad")]: true }]) {
    assert.throws(() => prepareRawContract({ ...valid, abi }), /raw ABI/);
  }
  assert.throws(() => prepareRawContract({ ...valid, abi: commandAbi({ target: "wasm128" }) }), /target/);
  for (const replacement of [
    null,
    { parameterShape: "none", returnShape: "exitCode" },
    { ...entryPoint(), extra: true },
    entryPoint("synchronous", "exitCode", "span"),
    entryPoint("synchronous", "decimal"),
    entryPoint("deferred"),
  ]) {
    assert.throws(() => prepareRawContract({
      ...valid,
      abi: commandAbi({ entryPoint: replacement }),
    }), /entry point|entry-point/);
  }
});

test("rejects malformed and duplicate physical descriptors", () => {
  const valid = { contractKey: commandComponentContract, providers: providers() };
  for (const imports of [
    null,
    [null],
    [functionImport(), { ...functionImport(), extra: true }],
    [{ module: "", name: "value", kind: "function" }],
    [{ module: "service", name: "", kind: "function" }],
    [{ module: "service", name: "value", kind: "record" }],
    [functionImport(), functionImport()],
  ]) {
    assert.throws(() => prepareRawContract({ ...valid, abi: commandAbi({ imports }) }), /raw import/);
  }
  for (const exports of [
    null,
    [null],
    [{ name: "run", kind: "function", extra: true }],
    [{ name: "", kind: "function" }],
    [{ name: "run", kind: "record" }],
    [functionExport("run"), functionExport("run")],
  ]) {
    assert.throws(() => prepareRawContract({ ...valid, abi: commandAbi({ exports }) }), /raw export/);
  }
});

test("rejects Preview 1 and wrong-target imports", () => {
  for (const module of [
    "wasi_snapshot_preview1",
    "wasi_unstable",
    "cm32p2|wasi_snapshot_preview1",
    "cm32p2|wasi_unstable",
  ]) {
    assert.throws(() => prepareRawContract({
      contractKey: commandComponentContract,
      abi: commandAbi({ imports: [functionImport(module, "value")] }),
      providers: { [module]: { value() {} } },
    }), /Preview 1/);
  }
  assert.throws(() => prepareRawContract({
    contractKey: commandComponentContract,
    abi: commandAbi({ imports: [functionImport("cm64p2|wasi:cli/stdout@0.2", "get-stdout")] }),
    providers: {},
  }), /target/);
});

test("rejects malformed and mixed command/process ABI shapes", () => {
  const bind = abi => prepareRawContract({
    contractKey: commandComponentContract,
    abi,
    providers: providers(),
  });
  assert.throws(() => bind(commandAbi({ exports: [] })), /run/);
  assert.throws(() => bind(commandAbi({ exports: [{ name: "run", kind: "memory" }] })), /run/);
  assert.throws(() => bind(commandAbi({ entryPoint: entryPoint("asynchronous") })), /synchronous/);
  for (const name of [
    ...["netwasm.process.status", "netwasm.process.result", "netwasm.process.complete"],
    "cm32p2|netwasm:runtime/reactor-guest@1|wake",
  ]) {
    assert.throws(() => bind(commandAbi({ exports: [functionExport("run"), functionExport(name)] })), /mixes/);
  }
  assert.throws(() => bind(commandAbi({
    imports: [functionImport("cm32p2|netwasm:runtime/reactor-host@1", "watch")],
  })), /mixes/);
});

test("rejects malformed process ABI shapes", () => {
  const bind = (abi, host = reactorHost()) => prepareRawContract({
    contractKey: processComponentContract,
    abi,
    providers: providers(),
    reactorHost: host,
  });
  assert.throws(() => bind(processAbi(), null), /unavailable/);
  assert.throws(() => bind(processAbi(), {}), /canonical/);
  assert.throws(() => bind(processAbi(), { watch() {}, cancel: 1 }), /canonical/);
  const reserved = processAbi();
  const reservedModule = "cm32p2|netwasm:runtime/reactor-host@1";
  assert.throws(() => prepareRawContract({
    contractKey: processComponentContract,
    abi: reserved,
    providers: { ...providers(), [reservedModule]: reactorHost() },
    reactorHost: reactorHost(),
  }), /reserved/);
  assert.throws(() => bind({ ...processAbi(), entryPoint: entryPoint("synchronous") }), /asynchronous/);
  for (const missing of ["netwasm.process.status", "netwasm.process.complete",
    "cm32p2|netwasm:runtime/reactor-guest@1|wake"]) {
    const abi = processAbi();
    abi.exports = abi.exports.filter(descriptor => descriptor.name !== missing);
    assert.throws(() => bind(abi), /required/);
  }
  const wrongKind = processAbi();
  wrongKind.exports = wrongKind.exports.map(descriptor => descriptor.name === "netwasm.process.status"
    ? { ...descriptor, kind: "memory" } : descriptor);
  assert.throws(() => bind(wrongKind), /required/);
  const extraResult = processAbi({ returnShape: "void" });
  extraResult.exports.push(functionExport("netwasm.process.result"));
  assert.throws(() => bind(extraResult), /result export/);
  const missingResult = processAbi();
  missingResult.exports = missingResult.exports.filter(descriptor => descriptor.name !== "netwasm.process.result");
  assert.throws(() => bind(missingResult), /result export/);
  const wrongWake = processAbi();
  wrongWake.exports.push(functionExport("cm64p2|netwasm:runtime/reactor-guest@1|wake"));
  assert.throws(() => bind(wrongWake), /wrong-target/);
  for (const descriptor of [
    { module: "cm32p2|netwasm:runtime/reactor-host@1", name: "watch", kind: "memory" },
    functionImport("cm32p2|netwasm:runtime/reactor-host@1", "other"),
  ]) {
    const abi = processAbi();
    abi.imports = [functionImport(), descriptor];
    assert.throws(() => bind(abi), /reactor-host import/);
  }
});

test("projects only enumerable data provider members", () => {
  const request = providers => prepareRawContract({
    contractKey: commandComponentContract,
    abi: commandAbi(),
    providers,
  });
  for (const value of [null, [], Object.create({}), { [Symbol("bad")]: true }]) {
    assert.throws(() => request(value), /providers/);
  }
  for (const source of [null, 1, {}, { "get-stdout": undefined },
    { "get-stdout": 1 },
    Object.defineProperty({}, "get-stdout", { get() { return () => {}; }, enumerable: true }),
    Object.defineProperty({}, "get-stdout", { value() {}, enumerable: false })]) {
    assert.throws(() => request({ "wasi:cli/stdout@0.2.11": source }), /provider|import/);
  }
  const getterProvider = Object.defineProperty({}, "wasi:cli/stdout@0.2.11", {
    get() { return providers()["wasi:cli/stdout@0.2.11"]; },
    enumerable: true,
  });
  assert.throws(() => request(getterProvider), /data property/);
  const nonFunctionAbi = commandAbi({
    imports: [{ module: "memory", name: "value", kind: "memory" }],
  });
  const plan = prepareRawContract({
    contractKey: commandComponentContract,
    abi: nonFunctionAbi,
    providers: { memory: { value: {} } },
  });
  assert.deepEqual(Object.keys(plan.imports.memory), ["value"]);
});

test("rejects malformed instantiated export boundaries", () => {
  const command = prepareRawContract({
    contractKey: commandComponentContract,
    abi: commandAbi({ imports: [] }),
    providers: {},
  });
  for (const instance of [null, {}, { exports: null }, { exports: {} },
    { exports: { run: 1 } },
    { exports: Object.defineProperty({}, "run", { get() { return () => 0; }, enumerable: true }) }]) {
    assert.throws(() => command.bind(instance), /exports|function|data property/);
  }
  const process = prepareRawContract({
    contractKey: processComponentContract,
    abi: processAbi(),
    providers: providers(),
    reactorHost: reactorHost(),
  });
  const complete = {
    run() { return 1; },
    "netwasm.process.status"() { return 1; },
    "netwasm.process.result"() { return 0; },
    "netwasm.process.complete"() {},
    "cm32p2|netwasm:runtime/reactor-guest@1|wake"() {},
  };
  for (const missing of Object.keys(complete)) {
    const exports = { ...complete };
    delete exports[missing];
    assert.throws(() => process.bind({ exports }), /unavailable/);
  }
});

test("rejects malformed execution-instance binding requests and plans", () => {
  const plan = buildRawAbiPlan({
    contractKey: commandComponentContract,
    abi: commandAbi({ imports: [] }),
  });
  const instance = { exports: { run() { return 0; } } };
  for (const request of [null, [], {}, { instance, plan, extra: true }]) {
    assert.throws(() => bindRawExecutionInstance(request), /request/);
  }
  for (const candidate of [
    null,
    [],
    {},
    { ...plan, extra: true },
    { ...plan, contractKey: "other" },
    { ...plan, entryPoint: null },
    { ...plan, invocationArguments: null },
    { ...plan, reactorGuestExport: null },
  ]) {
    assert.throws(() => bindRawExecutionInstance({ instance, plan: candidate }), /plan/);
  }
});
