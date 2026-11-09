import assert from "node:assert/strict";
import test from "node:test";
import { commandExecutionContract as commandComponentContract } from "./execution-contracts.mjs";
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
const asyncCommandAbi = ({ target = "wasm32", returnShape = "exitCode" } = {}) => ({
  target,
  entryPoint: entryPoint("asynchronous", returnShape, "stringArray"),
  imports: [functionImport()],
  exports: [
    functionExport("run"),
    functionExport("netwasm.entry.callback"),
    functionExport("netwasm.entry.exit_code"),
  ],
});
const providers = () => ({
  "wasi:cli/stdout@0.2.11": {
    "get-stdout"() { return "stdout"; },
    extra() {},
  },
  unused: { value: 1 },
});
function prepareRawContract({ contractKey, abi, providers: physicalProviders }) {
  const plan = buildRawAbiPlan({ contractKey, abi });
  const canonicalBinding = Object.freeze({
    imports: Object.freeze(Object.create(null)),
    target: plan.target,
  });
  const imports = composeRawImports({
    canonicalBinding,
    inventory: Object.freeze({
      target: plan.target,
      imports: plan.imports,
    }),
    physicalProviders,
  });
  return Object.freeze({
    contractKey: plan.contractKey,
    target: plan.target,
    imports,
    bind(instance, componentModel = null) { return bindRawExecutionInstance({ componentModel, instance, plan }); },
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

test("drives an asynchronous command entry through its callback and reads its exit code", async () => {
  for (const [target, zero] of [["wasm32", 0], ["wasm64", 0n]]) {
    const calls = [];
    const plan = prepareRawContract({
      contractKey: commandComponentContract,
      abi: asyncCommandAbi({ target }),
      providers: providers(),
    });
    const exports = {
      marker: target,
      run(value) { calls.push([this.marker, "start", value]); return 0x12; },
      "netwasm.entry.callback"(...event) { calls.push([this.marker, "callback", ...event]); return 0; },
      "netwasm.entry.exit_code"() { calls.push([this.marker, "exit"]); return -23; },
    };
    const componentModel = {
      async drive(code, callback) {
        calls.push([target, "drive", code]);
        assert.equal(callback(1, 2, 3), 0);
      },
    };
    const binding = plan.bind({ exports }, componentModel);
    assert.equal(await binding.command.run(), -23);
    assert.deepEqual(calls, [
      [target, "start", zero],
      [target, "drive", 0x12],
      [target, "callback", 1, 2, 3],
      [target, "exit"],
    ]);
  }
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

test("preserves distinct import tuples containing separator characters", () => {
  const plan = buildRawAbiPlan({
    contractKey: commandComponentContract,
    abi: commandAbi({ imports: [
      functionImport("host\u0000member", "tail"),
      functionImport("host", "member\u0000tail"),
    ] }),
  });
  assert.deepEqual(plan.imports, [
    functionImport("host\u0000member", "tail"),
    functionImport("host", "member\u0000tail"),
  ]);
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

test("rejects malformed command ABI shapes", () => {
  const bind = abi => prepareRawContract({
    contractKey: commandComponentContract,
    abi,
    providers: providers(),
  });
  assert.throws(() => bind(commandAbi({ exports: [] })), /run/);
  assert.throws(() => bind(commandAbi({ exports: [{ name: "run", kind: "memory" }] })), /run/);
  for (const name of ["netwasm.entry.callback", "netwasm.entry.exit_code"]) {
    assert.throws(() => bind(commandAbi({ exports: [functionExport("run"), functionExport(name)] })),
      /mixes asynchronous entry exports/);
    const abi = asyncCommandAbi();
    abi.exports = abi.exports.filter(descriptor => descriptor.name !== name);
    assert.throws(() => bind(abi), /required/);
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
  const asynchronous = prepareRawContract({
    contractKey: commandComponentContract,
    abi: asyncCommandAbi(),
    providers: providers(),
  });
  const complete = {
    run() { return 1; },
    "netwasm.entry.callback"() { return 0; },
    "netwasm.entry.exit_code"() { return 0; },
  };
  const componentModel = { drive: async () => {} };
  for (const missing of Object.keys(complete)) {
    const exports = { ...complete };
    delete exports[missing];
    assert.throws(() => asynchronous.bind({ exports }, componentModel), /unavailable/);
  }
  for (const invalid of [null, {}, { drive: 1 }]) {
    assert.throws(() => asynchronous.bind({ exports: complete }, invalid), /Component Model/);
  }
});

test("rejects malformed execution-instance binding requests and plans", () => {
  const plan = buildRawAbiPlan({
    contractKey: commandComponentContract,
    abi: commandAbi({ imports: [] }),
  });
  const instance = { exports: { run() { return 0; } } };
  for (const request of [null, [], {}, { componentModel: null, instance, plan, extra: true }]) {
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
  ]) {
    assert.throws(() => bindRawExecutionInstance({ componentModel: null, instance, plan: candidate }), /plan/);
  }
});
