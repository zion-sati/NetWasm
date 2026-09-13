import assert from "node:assert/strict";
import test from "node:test";
import {
  bindRawCanonicalImportInstance,
  closeRawCanonicalImportBinding,
  createRawCanonicalImportBinding,
} from "./raw-canonical-import-binding.mjs";
import { resourceDisposeSymbol } from "./resource-disposal.mjs";

const fingerprint = `sha256:${"1".repeat(64)}`;
const callableIdentity = { module: "cm32p2|sample:raw/api@1", name: "get-file" };
const resourceIdentity = { module: "cm32p2|sample:raw/api@1", name: "file_drop" };
const textIdentity = { module: "cm32p2|sample:raw/api@1", name: "get-text" };
const exportedNewIdentity = { module: "cm32p2|_ex_sample:raw/api@1", name: "file_new" };
const exportedDestructor = "cm32p2|sample:raw/api@1|file_dtor";
const reactorModule = "cm32p2|netwasm:runtime/reactor-host@1";
const watchIdentity = { module: reactorModule, name: "watch" };
const cancelIdentity = { module: reactorModule, name: "cancel" };

class File {
  constructor(released) {
    this.released = released;
  }

  [resourceDisposeSymbol]() {
    this.released.push(this);
  }
}

const callableBinding = () => ({
  kind: "callable",
  target: "wasm32",
  physical: callableIdentity,
  coreSignature: { parameters: [], results: ["i32"] },
  provider: {
    interface: "sample:raw@1/api",
    function: "get-file",
    javascriptName: "getFile",
    functionKind: "freestanding",
    resourceType: null,
    resourceName: null,
    resourceJavaScriptName: null,
  },
  parameters: [],
  result: { kind: "owned-resource", resourceType: 9 },
  canonicalSignature: {
    parameters: [], result: "i32", flatParameters: [], flatResults: ["i32"],
    indirectParameters: false, indirectResult: false,
  },
  parameterMemory: { size: 0, alignment: 1 },
  resultMemory: { size: 4, alignment: 4 },
});

const resourceBinding = () => ({
  kind: "resource",
  target: "wasm32",
  physical: resourceIdentity,
  coreSignature: { parameters: ["i32"], results: [] },
  resource: {
    destructor: null,
    interface: "sample:raw@1/api",
    intrinsic: "imported-resource-drop",
    javascriptName: "File",
    name: "file",
    type: 9,
  },
});

const textBinding = () => ({
  ...callableBinding(),
  physical: textIdentity,
  coreSignature: { parameters: ["i32"], results: [] },
  provider: {
    ...callableBinding().provider,
    function: "get-text",
    javascriptName: "getText",
  },
  result: { kind: "text" },
  canonicalSignature: {
    parameters: ["i32"], result: null, flatParameters: [], flatResults: ["i32", "i32"],
    indirectParameters: false, indirectResult: true,
  },
  resultMemory: { size: 8, alignment: 4 },
});

const exportedNewBinding = () => ({
  kind: "resource",
  target: "wasm32",
  physical: exportedNewIdentity,
  coreSignature: { parameters: ["i32"], results: ["i32"] },
  resource: {
    destructor: exportedDestructor,
    interface: "sample:raw@1/api",
    intrinsic: "exported-resource-new",
    javascriptName: "File",
    name: "file",
    type: 9,
  },
});

const reactorBinding = operation => ({
  kind: "callable",
  target: "wasm32",
  physical: operation === "watch" ? watchIdentity : cancelIdentity,
  coreSignature: {
    parameters: operation === "watch" ? ["i32", "i32"] : ["i32"],
    results: [],
  },
  provider: {
    interface: "netwasm:runtime/reactor-host@1.0.0",
    function: operation,
    javascriptName: operation,
    functionKind: "freestanding",
    resourceType: null,
    resourceName: null,
    resourceJavaScriptName: null,
  },
  parameters: operation === "watch"
    ? [
        { name: "ready", javascriptName: "ready", type: { kind: "owned-resource", resourceType: 9 } },
        { name: "token", javascriptName: "token", type: { kind: "u32" } },
      ]
    : [{ name: "token", javascriptName: "token", type: { kind: "u32" } }],
  result: null,
  canonicalSignature: {
    parameters: operation === "watch" ? ["i32", "i32"] : ["i32"],
    result: null,
    flatParameters: operation === "watch" ? ["i32", "i32"] : ["i32"],
    flatResults: [],
    indirectParameters: false,
    indirectResult: false,
  },
  parameterMemory: { size: operation === "watch" ? 8 : 4, alignment: 4 },
  resultMemory: null,
});

function generatedAdapter(options = {}) {
  const capabilities = options.capabilities ?? ["bindCallable", "bindResource"];
  const identities = options.identities ?? [callableIdentity, resourceIdentity];
  const metadata = options.metadata ?? {
    abiVersion: 1,
    target: "wasm32",
    witSourceFingerprint: fingerprint,
    bindingIdentities: identities,
    requiredCapabilities: capabilities,
  };
  return {
    rawAdapterMetadata: metadata,
    createAdapter(request) {
      options.observeRequest?.(request);
      if (options.product) return options.product(request, metadata);
      const imports = Object.create(null);
      for (const identity of identities) imports[identity.module] ??= Object.create(null);
      if (capabilities.includes("bindCallable")) {
        imports[callableIdentity.module][callableIdentity.name] = request.bindCallable(callableBinding());
      }
      if (capabilities.includes("bindResource")) {
        imports[resourceIdentity.module][resourceIdentity.name] = request.bindResource(resourceBinding());
      }
      for (const module of Object.values(imports)) Object.freeze(module);
      return Object.freeze({ metadata, imports: Object.freeze(imports) });
    },
  };
}

function instance(overrides = {}) {
  return {
    exports: {
      cm32p2_memory: new WebAssembly.Memory({ initial: 1 }),
      cm32p2_realloc() { return 8; },
      ...overrides,
    },
  };
}

test("composes selected generated imports and owns their resource lifetime", () => {
  const released = [];
  let providerCalls = 0;
  let adapterRequest;
  const adapter = generatedAdapter({ observeRequest: value => { adapterRequest = value; } });
  const binding = createRawCanonicalImportBinding({
    adapter,
    reactor: null,
    providers: {
      "sample:raw@1/api": {
        File,
        getFile() { providerCalls += 1; return new File(released); },
      },
    },
  });
  assert.equal(binding.target, "wasm32");
  assert.deepEqual(Object.keys(adapterRequest).sort(), ["bindCallable", "bindResource", "metadata"]);
  const imports = binding.imports[callableIdentity.module];
  assert.throws(() => imports[callableIdentity.name](), /not bound/);
  assert.equal(providerCalls, 0);
  assert.throws(() => imports[resourceIdentity.name](1), /not bound/);

  bindRawCanonicalImportInstance({ binding, instance: instance() });
  const handle = imports[callableIdentity.name]();
  assert.equal(providerCalls, 1);
  assert.equal(released.length, 0);
  imports[resourceIdentity.name](handle);
  assert.equal(released.length, 1);
  closeRawCanonicalImportBinding({ binding });
  assert.throws(() => imports[callableIdentity.name](), /closed/);
  assert.throws(() => closeRawCanonicalImportBinding({ binding }), /closed/);
  assert.throws(() => bindRawCanonicalImportInstance({ binding, instance: instance() }), /closed/);
});

test("requests only the selected callable, resource or empty capability subset", () => {
  for (const capabilities of [[], ["bindCallable"], ["bindResource"], ["bindCallable", "bindResource"]]) {
    let keys;
    const identities = capabilities.length === 0
      ? []
      : capabilities.length === 1
        ? [capabilities[0] === "bindCallable" ? callableIdentity : resourceIdentity]
        : [callableIdentity, resourceIdentity];
    const binding = createRawCanonicalImportBinding({
      adapter: generatedAdapter({
        capabilities,
        identities,
        observeRequest: request => { keys = Object.keys(request).sort(); },
      }),
      reactor: null,
      providers: { "sample:raw@1/api": { getFile() { return {}; } } },
    });
    assert.deepEqual(keys, ["metadata", ...capabilities].sort());
    closeRawCanonicalImportBinding({ binding });
  }
});

test("closes outstanding provider resources after binding", () => {
  const released = [];
  const binding = createRawCanonicalImportBinding({
    adapter: generatedAdapter(),
    reactor: null,
    providers: {
      "sample:raw@1/api": { getFile() { return new File(released); } },
    },
  });
  bindRawCanonicalImportInstance({ binding, instance: instance() });
  binding.imports[callableIdentity.module][callableIdentity.name]();
  closeRawCanonicalImportBinding({ binding });
  assert.equal(released.length, 1);
});

test("uses the deferred canonical reallocator for an indirect text result", () => {
  const adapter = generatedAdapter({
    capabilities: ["bindCallable"],
    identities: [textIdentity],
    product(request, metadata) {
      const module = Object.freeze({ [textIdentity.name]: request.bindCallable(textBinding()) });
      return Object.freeze({
        imports: Object.freeze({ [textIdentity.module]: module }),
        metadata,
      });
    },
  });
  const binding = createRawCanonicalImportBinding({
    adapter,
    reactor: null,
    providers: { "sample:raw@1/api": { getText() { return "hi"; } } },
  });
  const value = instance({ cm32p2_realloc() { return 64; } });
  bindRawCanonicalImportInstance({ binding, instance: value });
  binding.imports[textIdentity.module][textIdentity.name](0);
  const view = new DataView(value.exports.cm32p2_memory.buffer);
  assert.equal(view.getUint32(0, true), 64);
  assert.equal(view.getUint32(4, true), 2);
  assert.deepEqual([...new Uint8Array(value.exports.cm32p2_memory.buffer, 64, 2)], [104, 105]);
  closeRawCanonicalImportBinding({ binding });
});

test("requires and invokes an exact exported-resource destructor on close", () => {
  const destroyed = [];
  const adapter = generatedAdapter({
    capabilities: ["bindResource"],
    identities: [exportedNewIdentity],
    product(request, metadata) {
      const module = Object.freeze({
        [exportedNewIdentity.name]: request.bindResource(exportedNewBinding()),
      });
      return Object.freeze({
        imports: Object.freeze({ [exportedNewIdentity.module]: module }),
        metadata,
      });
    },
  });
  const binding = createRawCanonicalImportBinding({ adapter, providers: {}, reactor: null });
  bindRawCanonicalImportInstance({
    binding,
    instance: instance({ [exportedDestructor](value) { destroyed.push(value); } }),
  });
  const handle = binding.imports[exportedNewIdentity.module][exportedNewIdentity.name](42);
  assert.equal(handle, 1);
  closeRawCanonicalImportBinding({ binding });
  assert.deepEqual(destroyed, [42]);
});

test("binds selected reactor calls through the owned resource store", () => {
  const calls = [];
  let available = false;
  const adapter = generatedAdapter({
    capabilities: ["bindCallable", "bindReactor"],
    identities: [callableIdentity, watchIdentity, cancelIdentity],
    product(request, metadata) {
      return Object.freeze({
        imports: Object.freeze({
          [callableIdentity.module]: Object.freeze({
            [callableIdentity.name]: request.bindCallable(callableBinding()),
          }),
          [reactorModule]: Object.freeze({
            watch: request.bindReactor(reactorBinding("watch")),
            cancel: request.bindReactor(reactorBinding("cancel")),
          }),
        }),
        metadata,
      });
    },
  });
  const binding = createRawCanonicalImportBinding({
    adapter,
    providers: {
      "sample:raw@1/api": {
        getFile() { return new File(calls); },
      },
    },
    reactor: {
      module: reactorModule,
      assertAvailable() {
        if (!available) throw new Error("not available");
      },
      watch(pollable, token) { calls.push(["watch", pollable, token]); },
      cancel(token) { calls.push(["cancel", token]); },
    },
  });
  assert.throws(() => binding.imports[reactorModule].cancel(1), /not available/);
  bindRawCanonicalImportInstance({ binding, instance: instance() });
  available = true;
  const pollable = binding.imports[callableIdentity.module][callableIdentity.name]();
  binding.imports[reactorModule].watch(pollable, 17);
  binding.imports[reactorModule].cancel(17);
  assert.equal(calls.length, 2);
  assert.equal(calls[0][0], "watch");
  assert.equal(calls[0][2], 17);
  assert.deepEqual(calls[1], ["cancel", 17]);
  closeRawCanonicalImportBinding({ binding });
  assert.equal(calls.length, 2);
});

test("rejects invalid factory, adapter namespace and metadata contracts", () => {
  const valid = { adapter: generatedAdapter(), providers: {}, reactor: null };
  for (const request of [null, [], {}, { ...valid, extra: true }, { ...valid, adapter: null }]) {
    assert.throws(() => createRawCanonicalImportBinding(request), TypeError);
  }
  assert.throws(() => createRawCanonicalImportBinding({
    ...valid,
    reactor: {},
  }), /undeclared reactor/);
  assert.throws(() => createRawCanonicalImportBinding({
    ...valid,
    adapter: Object.defineProperty({}, "rawAdapterMetadata", { get() { return {}; } }),
  }), /data property/);
  assert.throws(() => createRawCanonicalImportBinding({
    ...valid,
    adapter: { rawAdapterMetadata: {}, createAdapter: null },
  }), /factory/);

  assert.throws(() => createRawCanonicalImportBinding({
    ...valid,
    adapter: { rawAdapterMetadata: generatedAdapter().rawAdapterMetadata },
  }), /factory/);

  const metadata = generatedAdapter().rawAdapterMetadata;
  const invalidMetadata = [
    null,
    [],
    {},
    { ...metadata, extra: true },
    { ...metadata, abiVersion: 2 },
    { ...metadata, target: "wasm128" },
    { ...metadata, witSourceFingerprint: "sha256:bad" },
    { ...metadata, bindingIdentities: null },
    { ...metadata, bindingIdentities: [null] },
    { ...metadata, bindingIdentities: [{ ...callableIdentity, extra: true }] },
    { ...metadata, bindingIdentities: [{ ...callableIdentity, module: "" }] },
    { ...metadata, bindingIdentities: [{ ...callableIdentity, name: "" }] },
    { ...metadata, bindingIdentities: [callableIdentity, callableIdentity] },
    { ...metadata, requiredCapabilities: null },
    { ...metadata, requiredCapabilities: ["bindResource", "bindCallable"] },
    { ...metadata, requiredCapabilities: ["bindCallable", "bindCallable"] },
    { ...metadata, requiredCapabilities: ["other"] },
    { ...metadata, requiredCapabilities: ["bindCallable", "bindResource", "other"] },
  ];
  for (const candidate of invalidMetadata) {
    assert.throws(() => createRawCanonicalImportBinding({
      adapter: generatedAdapter({ metadata: candidate }),
      providers: {},
      reactor: null,
    }), TypeError);
  }
});

test("rejects invalid generated adapter products and exact import inventories", () => {
  const metadata = generatedAdapter().rawAdapterMetadata;
  const product = imports => (_request, actualMetadata) => ({ imports, metadata: actualMetadata });
  const module = Object.freeze({
    [callableIdentity.name]() {},
    [resourceIdentity.name]() {},
  });
  const validImports = Object.freeze({ [callableIdentity.module]: module });
  const cases = [
    () => null,
    () => [],
    () => ({}),
    () => ({ metadata: {}, imports: validImports }),
    product(null),
    product({}),
    product({ [callableIdentity.module]: null }),
    product({ [callableIdentity.module]: { [callableIdentity.name]() {} } }),
    product({ [callableIdentity.module]: {
      [callableIdentity.name]() {}, [resourceIdentity.name]() {}, extra() {},
    } }),
    product({ [callableIdentity.module]: {
      [callableIdentity.name]: 1, [resourceIdentity.name]() {},
    } }),
    product({ [callableIdentity.module]: Object.defineProperty({
      [resourceIdentity.name]() {},
    }, callableIdentity.name, { get() { return () => {}; }, enumerable: true }) }),
    product({ [callableIdentity.module]: { ...module, [Symbol("extra")]: true } }),
  ];
  for (const create of cases) {
    assert.throws(() => createRawCanonicalImportBinding({
      adapter: generatedAdapter({ product: create }),
      providers: {},
      reactor: null,
    }), TypeError);
  }
});

test("rejects invalid bind and close requests and duplicate instance binding", () => {
  const binding = createRawCanonicalImportBinding({
    adapter: generatedAdapter({ capabilities: [], identities: [] }),
    providers: {},
    reactor: null,
  });
  for (const request of [null, [], {}, { binding: {} }, { binding, instance: {}, extra: true }]) {
    assert.throws(() => bindRawCanonicalImportInstance(request), TypeError);
  }
  bindRawCanonicalImportInstance({ binding, instance: instance() });
  assert.throws(() => bindRawCanonicalImportInstance({ binding, instance: instance() }), /already bound/);
  for (const request of [null, [], {}, { binding: {} }, { binding, extra: true }]) {
    assert.throws(() => closeRawCanonicalImportBinding(request), TypeError);
  }
  closeRawCanonicalImportBinding({ binding });
});
