import assert from "node:assert/strict";
import test from "node:test";

import { NetWasmHostError } from "./managed-errors.mjs";
import {
  bindNetWasmInterop,
  disposeNetWasmInterop,
  prepareNetWasmInterop,
  prepareParsedNetWasmInterop,
  prepareRawNetWasmInterop,
} from "./raw-interop-preparation.mjs";

const builtinModule = "netwasm.host.v1";
const statusAbi = Object.freeze({
  successStatus: 0,
  hostFailureStatus: 1,
  scalarResultOffset: 0,
});
const targetLayout = Object.freeze({
  managedReferenceSize: 4,
  stringLengthOffset: 4,
  stringDataOffset: 8,
  arrayLengthOffset: 4,
  arrayDataPointerOffset: 8,
});

test("composes imports, binds one managed boundary, and closes pending work", async () => {
  const memory = new WebAssembly.Memory({ initial: 1 });
  const registered = [];
  const cancelled = [];
  const runtime = {
    memory,
    native_alloc: () => 1024,
    native_free: address => registered.push(["free", address]),
    stack_trace_register_symbol: (...values) => registered.push(["register", ...values]),
  };
  const contract = manifest({
    imports: [
      importDescriptor("consumer", "sync_i32", "i32"),
      {
        ...importDescriptor("consumer", "async_wait", "void"),
        asyncReturn: "task",
        resolveExport: "resolve_wait",
        rejectExport: "reject_wait",
        cancelExport: "cancel_wait",
      },
      importDescriptor(builtinModule, "queue_microtask", "subscription", ["callback"]),
    ],
    exports: [
      exportDescriptor("managed_sync", "i32"),
      {
        ...exportDescriptor("managed_async", "i32"),
        asyncReturn: "task",
        statusExport: "managed_async_status",
        completeExport: "managed_async_complete",
        resultExport: "managed_async_result",
      },
      {
        ...exportDescriptor("managed_void_async", "void"),
        asyncReturn: "value-task",
        statusExport: "managed_void_status",
        completeExport: "managed_void_complete",
      },
    ],
    callbacks: [{
      module: builtinModule,
      importName: "queue_microtask",
      parameterIndex: 0,
      exportName: "invoke_queue",
      parameters: [],
      result: "void",
    }],
  });
  const raw = prepareRawNetWasmInterop({
    manifest: contract,
    runtimeModules: {
      consumer: { existing: () => 3 },
      "netwasm.runtime.v1": runtime,
    },
    builtinServices: { custom: () => 5 },
    consumerModules: {
      consumer: {
        sync_i32: () => 42,
        async_wait: () => new Promise(() => {}),
      },
    },
    diagnosticArtifacts: null,
    managedExceptionReporting: reporting(),
    stackTraceSymbols: {
      schemaVersion: 1,
      methods: [{ id: 7, name: "Managed.Entry" }],
    },
  });
  assert.equal(Object.isFrozen(raw), true);
  assert.equal(raw.imports.consumer.existing(), 3);
  assert.equal(raw.imports[builtinModule].custom(), 5);
  assert.equal(typeof raw.imports[builtinModule].queue_microtask, "function");

  const instance = {
    exports: {
      memory,
      managed_sync: () => 7,
      managed_async: () => 1,
      managed_async_status: () => 1,
      managed_async_complete: () => {},
      managed_async_result: () => 8,
      managed_void_async: () => 2,
      managed_void_status: () => 1,
      managed_void_complete: () => {},
      invoke_queue: () => {},
      resolve_wait: () => {},
      reject_wait: () => {},
      cancel_wait: handle => cancelled.push(handle),
    },
  };
  const boundary = raw.bindInstance(instance);
  assert.equal(boundary.instance, instance);
  assert.equal(boundary.adapter.memory, memory);
  assert.deepEqual(Object.keys(boundary.exports).sort(), [
    "managed_async", "managed_sync", "managed_void_async",
  ]);
  assert.equal(boundary.exports.managed_sync(), 7);
  assert.equal(raw.imports.consumer.sync_i32(0), statusAbi.successStatus);
  assert.equal(new DataView(memory.buffer).getInt32(0, true), 42);
  assert.equal(raw.imports.consumer.async_wait(31, 4), statusAbi.successStatus);
  assert.deepEqual(registered, [["register", 7, 1024, 13], ["free", 1024]]);

  raw.close();
  raw.close();
  assert.deepEqual(cancelled, [31]);
  assert.equal(boundary.handles.count, 0);
});

test("supports runtime-owned memory and both compatibility close facades", () => {
  const memory = new WebAssembly.Memory({ initial: 1 });
  const preparation = prepareParsedNetWasmInterop({
    manifest: manifest(),
    runtimeModules: { "netwasm.runtime.v1": { memory } },
    parsedStackTraceSymbols: [],
  });
  const boundary = bindNetWasmInterop({ preparation, instance: { exports: {} } });
  assert.equal(boundary.adapter.memory, memory);
  boundary.dispose();
  boundary.dispose();

  const second = prepareParsedNetWasmInterop({
    manifest: manifest(), runtimeModules: {}, parsedStackTraceSymbols: [],
  });
  bindNetWasmInterop({
    preparation: second,
    instance: { exports: { memory: new WebAssembly.Memory({ initial: 1 }) } },
  });
  disposeNetWasmInterop({ preparation: second });
});

test("rejects reserved or missing consumer services before instantiation", () => {
  assert.throws(() => prepareNetWasmInterop({
    manifest: manifest(),
    runtimeModules: {},
    consumerModules: { [builtinModule]: {} },
  }), error => error instanceof NetWasmHostError && /cannot be supplied/.test(error.message));

  assert.throws(() => prepareParsedNetWasmInterop({
    manifest: manifest({ imports: [importDescriptor("missing", "call", "void")] }),
    runtimeModules: {},
    parsedStackTraceSymbols: [],
  }), error => error instanceof NetWasmHostError && /missing host service/.test(error.message));
});

test("rejects missing memory and every declared managed instance family", () => {
  expectBindFailure(manifest(), {}, /did not expose memory/);
  expectBindFailure(manifest({
    exports: [exportDescriptor("managed", "void")],
  }), { memory: new WebAssembly.Memory({ initial: 1 }) }, /managed export managed/);
  expectBindFailure(manifest({
    exports: [{
      ...exportDescriptor("managed", "i32"),
      asyncReturn: "task",
      statusExport: "status",
      completeExport: "complete",
      resultExport: "result",
    }],
  }), {
    memory: new WebAssembly.Memory({ initial: 1 }),
    managed: () => 1,
    complete: () => {},
    result: () => 1,
  }, /async export helper status/);
  expectBindFailure(manifest({
    imports: [importDescriptor("consumer", "listen", "subscription", ["callback"])],
    callbacks: [{
      module: "consumer", importName: "listen", parameterIndex: 0,
      exportName: "invoke", parameters: [], result: "void",
    }],
  }), { memory: new WebAssembly.Memory({ initial: 1 }) }, /managed callback invoke/, {
    consumer: { listen: () => ({ dispose() {} }) },
  });
  expectBindFailure(manifest({
    imports: [{
      ...importDescriptor("consumer", "wait", "void"),
      asyncReturn: "task",
      resolveExport: "resolve",
      rejectExport: "reject",
      cancelExport: "cancel",
    }],
  }), { memory: new WebAssembly.Memory({ initial: 1 }) }, /async import helper resolve/, {
    consumer: { wait: () => Promise.resolve() },
  });
});

test("routes function diagnostic artifacts through configured reporting", async () => {
  const immediate = [];
  const enriched = [];
  const fixture = bindReporter({
    diagnosticArtifacts: () => ({}),
    managedExceptionReporting: {
      maximumMessageLength: 9,
      reportImmediate: event => immediate.push(event),
      reportEnriched: event => enriched.push(event),
    },
  });
  fixture.report(1, 0, 0);
  await settleReporting();
  assert.equal(immediate[0].message, null);
  assert.match(enriched[0].artifactError, /invalid diagnostic artifact manifest/);
  fixture.close();
});

test("uses default diagnostic reporting for deployed artifact objects", async () => {
  const messages = [];
  const original = console.error;
  console.error = message => messages.push(message);
  try {
    const fixture = bindReporter({
      diagnosticArtifacts: {},
      managedExceptionReporting: { maximumMessageLength: 1 },
    });
    const units = new Uint16Array(fixture.memory.buffer, 72, 3);
    units.set([97, 98, 99]);
    fixture.report(2, 0, 0);
    fixture.report(2, 64, 3);
    await settleReporting();
    assert.equal(messages.length, 4);
    assert.match(messages[0], /<no stored message>/);
    assert.match(messages[1], /a \(message truncated by host policy\)/);
    assert.match(messages[2], /unavailable/);
    assert.match(messages[3], /unavailable/);
    fixture.close();
  } finally {
    console.error = original;
  }
});

test("reports absent diagnostic artifacts through the configured failure path", async () => {
  const enriched = [];
  const fixture = bindReporter({
    managedExceptionReporting: {
      maximumMessageLength: 0,
      reportImmediate: () => {},
      reportEnriched: event => enriched.push(event),
    },
  });
  const units = new Uint16Array(fixture.memory.buffer, 72, 3);
  units.set([97, 98, 99]);
  fixture.report(3, 64, 3);
  await settleReporting();
  assert.match(enriched[0].artifactError, /were not deployed/);
  fixture.close();
});

function bindReporter(options) {
  const memory = new WebAssembly.Memory({ initial: 1 });
  const raw = prepareRawNetWasmInterop({
    manifest: manifest(),
    runtimeModules: {},
    ...options,
  });
  raw.bindInstance({ exports: { memory } });
  return {
    memory,
    report: raw.imports[builtinModule].report_terminal_exception_v1,
    close: raw.close,
  };
}

function expectBindFailure(contract, exports, pattern, consumerModules = {}) {
  const preparation = prepareParsedNetWasmInterop({
    manifest: contract,
    runtimeModules: {},
    consumerModules,
    managedExceptionReporting: reporting(),
    parsedStackTraceSymbols: [],
  });
  assert.throws(
    () => bindNetWasmInterop({ preparation, instance: { exports } }),
    error => error instanceof NetWasmHostError && pattern.test(error.message));
}

function manifest({ imports = [], exports = [], callbacks } = {}) {
  return {
    version: 1,
    target: "wasm32",
    statusAbi,
    targetLayout,
    imports,
    exports,
    ...(callbacks === undefined ? {} : { callbacks }),
  };
}

function importDescriptor(module, name, result, parameters = []) {
  return { module, name, parameters, result };
}

function exportDescriptor(name, result, parameters = []) {
  return { name, parameters, result };
}

function reporting() {
  return {
    reportImmediate() {},
    reportEnriched() {},
  };
}

async function settleReporting() {
  await new Promise(resolve => setImmediate(resolve));
}
