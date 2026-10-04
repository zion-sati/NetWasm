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

test("observes external callbacks but preserves recovered cross-service callback failures", async () => {
  let callback;
  let fail = false;
  let calls = 0;
  const observations = [];
  const preparation = prepareRawNetWasmInterop({
    manifest: manifest({
      imports: [
        importDescriptor("consumer", "subscribe", "subscription", ["callback"]),
        importDescriptor("consumer", "invoke", "void"),
      ],
      callbacks: [{
        module: "consumer", importName: "subscribe", parameterIndex: 0,
        exportName: "callback", parameters: [], result: "void",
      }],
    }),
    runtimeModules: {},
    consumerModules: { consumer: {
      subscribe(value) { callback = value; return { dispose() {} }; },
      invoke() { callback(); },
    } },
    observeAsyncCompletion: error => observations.push(error),
    managedExceptionReporting: reporting(),
  });
  preparation.bindInstance({ exports: {
    memory: new WebAssembly.Memory({ initial: 1 }),
    callback() { calls++; if (fail) throw new Error("callback failure"); },
    handle_release() {},
  } });
  assert.equal(preparation.imports.consumer.subscribe(7, 0), 0);
  fail = true;
  // Another import reenters the stored callback. The managed caller receives
  // a failure status and can catch it without a later terminal host failure.
  assert.equal(preparation.imports.consumer.invoke(0), 1);
  assert.deepEqual(observations, []);
  await new Promise(resolve => setImmediate(resolve));
  assert.deepEqual(observations, [undefined]);
  let externalFailure;
  assert.throws(() => callback(), error => { externalFailure = error; return true; });
  await new Promise(resolve => setImmediate(resolve));
  assert.deepEqual(observations, [undefined, externalFailure]);
  fail = false;
  callback();
  preparation.close();
  await new Promise(resolve => setImmediate(resolve));
  assert.deepEqual(observations, [undefined, externalFailure]);
  assert.equal(calls, 3);
  assert.throws(() => callback(), /closed/);
  await new Promise(resolve => setImmediate(resolve));
  assert.equal(calls, 3);
  assert.equal(observations.length, 2);
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
        completeExport: "managed_async_complete", completionResult: "exception-handle-v1",
        resultExport: "managed_async_result",
      },
      {
        ...exportDescriptor("managed_void_async", "void"),
        asyncReturn: "value-task",
        statusExport: "managed_void_status",
        completeExport: "managed_void_complete", completionResult: "exception-handle-v1",
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
  assert.equal(raw.consumeTerminalEvent(), undefined);
  await raw.drainTerminalReports();
  assert.equal(raw.imports.consumer.existing(), 3);
  assert.equal(raw.imports[builtinModule].custom(), 5);
  assert.equal(typeof raw.imports[builtinModule].queue_microtask, "function");

  const instance = {
    exports: {
      memory,
      managed_sync: () => 7,
      managed_async: () => 1,
      managed_async_status: () => 1,
      managed_async_complete: () => 0,
      managed_async_result: () => 8,
      managed_void_async: () => 2,
      managed_void_status: () => 1,
      managed_void_complete: () => 0,
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

test("closes pending export observations and rejects retained exports after close", async () => {
  const memory = new WebAssembly.Memory({ initial: 1 });
  let polls = 0;
  let completed = 0;
  const preparation = prepareRawNetWasmInterop({
    manifest: manifest({ exports: [
      exportDescriptor("sync", "i32"),
      {
        ...exportDescriptor("wait", "i32"),
        asyncReturn: "task", statusExport: "status", resultExport: "result", completeExport: "complete", completionResult: "exception-handle-v1",
      },
    ] }),
    runtimeModules: {},
  });
  const boundary = preparation.bindInstance({ exports: {
    cm32p2_memory: memory,
    sync: () => 7,
    wait: () => 17,
    status: () => { polls++; return 0; },
    result: () => 42,
    complete(handle) { assert.equal(handle, 17); completed++; return 0; },
  } });
  assert.equal(boundary.adapter.memory, memory);
  assert.equal(boundary.exports.sync(), 7);
  const pending = boundary.exports.wait();
  const rejected = assert.rejects(pending, error => error instanceof NetWasmHostError && /closed/.test(error.message));
  preparation.close();
  preparation.close();
  await rejected;
  const closedPolls = polls;
  await new Promise(resolve => setTimeout(resolve, 5));
  assert.equal(polls, closedPolls);
  assert.equal(completed, 1);
  assert.throws(() => boundary.exports.sync(), /session is closed/);
  assert.throws(() => boundary.exports.wait(), /session is closed/);
});

test("copies a faulted export payload without terminal reporting and keeps the instance usable", async () => {
  const memory = new WebAssembly.Memory({ initial: 1 });
  const message = "worker-probe 🌏";
  new Uint16Array(memory.buffer, 40, message.length)
    .set(Array.from({ length: message.length }, (_, index) => message.charCodeAt(index)));
  const trace = "at method#7";
  new Uint16Array(memory.buffer, 136, trace.length)
    .set(Array.from(trace, value => value.charCodeAt(0)));
  let completed = 0;
  const preparation = prepareRawNetWasmInterop({
    manifest: manifest({ exports: [
      exportDescriptor("sync", "i32"),
      { ...exportDescriptor("fail", "i32"), asyncReturn: "task", statusExport: "status",
        resultExport: "result", completeExport: "complete", completionResult: "exception-handle-v1" },
    ] }),
    runtimeModules: {},
    stackTraceSymbols: { schemaVersion: 1, methods: [{ id: 7, name: "Program.Fail in Program.cs:line 12" }] },
    managedExceptionReporting: {
      reportImmediate() { assert.fail("caught export reported as terminal"); },
      reportEnriched() { assert.fail("caught export enriched as terminal"); },
    },
  });
  const boundary = preparation.bindInstance({ exports: {
    memory, sync: () => 42, fail: () => 17, status: () => 2,
    result() { assert.fail("fault has no result"); },
    complete(handle) {
      assert.equal(handle, 17); completed++;
      const captured = preparation.imports[builtinModule].capture_managed_exception_v1(7, 32, message.length, 128, trace.length);
      new Uint8Array(memory.buffer).fill(0);
      return captured;
    },
  } });
  await assert.rejects(boundary.exports.fail(), error => error.message === message
    && error.managed.typeId === 7 && error.managed.typeName === null
    && error.managed.stackTrace === "at Program.Fail in Program.cs:line 12");
  assert.equal(completed, 1);
  assert.equal(boundary.handles.count, 0);
  assert.equal(preparation.consumeTerminalEvent(), undefined);
  await preparation.drainTerminalReports();
  assert.equal(boundary.exports.sync(), 42);
  preparation.close();
});

test("attempts every import and export release when a guest cancellation fails", async () => {
  const memory = new WebAssembly.Memory({ initial: 1 });
  const calls = [];
  const resolutions = [];
  const failure = new Error("cancel failed");
  const imports = ["first", "second"].map(name => ({
    ...importDescriptor("consumer", name, "void"),
    asyncReturn: "task", resolveExport: `${name}_resolve`, rejectExport: `${name}_reject`, cancelExport: `${name}_cancel`,
  }));
  const preparation = prepareRawNetWasmInterop({
    manifest: manifest({ imports, exports: [{
      ...exportDescriptor("wait", "void"),
      asyncReturn: "task", statusExport: "status", completeExport: "complete", completionResult: "exception-handle-v1",
    }] }),
    runtimeModules: {},
    consumerModules: { consumer: {
      first: () => new Promise(resolve => resolutions.push(resolve)),
      second: () => new Promise(resolve => resolutions.push(resolve)),
    } },
  });
  const boundary = preparation.bindInstance({ exports: {
    memory, wait: () => 9, status: () => 0,
    complete: () => { calls.push("export-complete"); return 0; },
    first_resolve: () => { calls.push("first-resolve"); },
    second_resolve: () => { calls.push("second-resolve"); },
    first_reject() {}, second_reject() {},
    first_cancel() { calls.push("first-cancel"); throw failure; },
    second_cancel() { calls.push("second-cancel"); },
  } });
  preparation.imports.consumer.first(1, 0);
  preparation.imports.consumer.second(2, 0);
  const rejected = assert.rejects(boundary.exports.wait(), /session is closed/);
  assert.throws(() => preparation.close(), error => error instanceof AggregateError && error.errors[0] === failure);
  preparation.close();
  await rejected;
  assert.deepEqual(calls, ["first-cancel", "second-cancel", "export-complete"]);
  assert.equal(boundary.handles.count, 0);
  for (const resolve of resolutions) resolve();
  await Promise.resolve();
  assert.deepEqual(calls, ["first-cancel", "second-cancel", "export-complete"]);
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
      completeExport: "complete", completionResult: "exception-handle-v1",
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

test("routes session availability and asynchronous completion failures", async () => {
  const failure = new Error("session failed");
  const completionFailure = new Error("completion failed");
  const observed = [];
  let available = true;
  const contract = manifest({
    imports: [{
      ...importDescriptor("consumer", "wait", "i32"),
      asyncReturn: "task", resolveExport: "resolve", rejectExport: "reject",
      cancelExport: "cancel",
    }],
    exports: [exportDescriptor("read", "i32")],
  });
  const preparation = prepareParsedNetWasmInterop({
    manifest: contract,
    runtimeModules: {},
    consumerModules: { consumer: { wait: () => Promise.resolve(42) } },
    parsedStackTraceSymbols: [],
    assertAvailable() { if (!available) throw failure; },
    observeAsyncFailure: cause => observed.push(cause),
  });
  const memory = new WebAssembly.Memory({ initial: 1 });
  const boundary = bindNetWasmInterop({ preparation, instance: { exports: {
    memory,
    read: () => 7,
    resolve() { throw completionFailure; },
    reject() {},
    cancel() {},
  } } });
  assert.equal(boundary.exports.read(), 7);
  available = false;
  assert.throws(() => boundary.exports.read(), error => error === failure);
  assert.equal(preparation.imports.consumer.wait(3, 0), statusAbi.successStatus);
  await settleReporting();
  assert.deepEqual(observed, [completionFailure]);
  boundary.dispose();
});

test("gates already-queued host completions after the first terminal failure", async () => {
  const hostResolutions = [];
  const guestCompletionHandles = [];
  const observed = [];
  let terminalFailure = null;
  const imports = ["first", "second"].map(name => ({
    ...importDescriptor("consumer", name, "i32"),
    asyncReturn: "task",
    resolveExport: `${name}_resolve`,
    rejectExport: `${name}_reject`,
    cancelExport: `${name}_cancel`,
  }));
  const preparation = prepareRawNetWasmInterop({
    manifest: manifest({ imports }),
    runtimeModules: {},
    consumerModules: { consumer: {
      first: () => new Promise(resolve => hostResolutions.push(resolve)),
      second: () => new Promise(resolve => hostResolutions.push(resolve)),
    } },
    assertAsyncDeliveryAvailable() {
      if (terminalFailure !== null) throw terminalFailure;
    },
    observeAsyncFailure(cause) {
      terminalFailure ??= cause;
      observed.push(cause);
    },
  });
  preparation.bindInstance({ exports: {
    memory: new WebAssembly.Memory({ initial: 1 }),
    first_resolve(handle) {
      guestCompletionHandles.push(handle);
      throw new Error("first completion failed");
    },
    second_resolve(handle) { guestCompletionHandles.push(handle); },
    first_reject() {}, second_reject() {},
    first_cancel() {}, second_cancel() {},
  } });

  assert.equal(preparation.imports.consumer.first(1, 0), statusAbi.successStatus);
  assert.equal(preparation.imports.consumer.second(2, 0), statusAbi.successStatus);
  hostResolutions[0](11);
  hostResolutions[1](22);
  await settleReporting();

  assert.deepEqual(guestCompletionHandles, [1]);
  assert.equal(observed.length, 2);
  assert.equal(observed[0].message, "first completion failed");
  assert.equal(observed[1], terminalFailure);
  preparation.close();
});

test("contains completion failures through both default lifetime observers", async () => {
  const contract = manifest({
    imports: [{
      ...importDescriptor("consumer", "wait", "i32"),
      asyncReturn: "task", resolveExport: "resolve", rejectExport: "reject",
      cancelExport: "cancel",
    }],
    exports: [exportDescriptor("read", "i32")],
  });
  for (const prepare of [
    () => prepareParsedNetWasmInterop({
      manifest: contract,
      runtimeModules: {},
      consumerModules: { consumer: { wait: () => Promise.resolve(1) } },
      parsedStackTraceSymbols: [],
    }),
    () => prepareNetWasmInterop({
      manifest: contract,
      runtimeModules: {},
      consumerModules: { consumer: { wait: () => Promise.resolve(1) } },
    }),
  ]) {
    const preparation = prepare();
    const boundary = bindNetWasmInterop({ preparation, instance: { exports: {
      memory: new WebAssembly.Memory({ initial: 1 }),
      read: () => 9,
      resolve() { throw new Error("contained"); },
      reject() {},
      cancel() {},
    } } });
    assert.equal(boundary.exports.read(), 9);
    assert.equal(preparation.imports.consumer.wait(5, 0), statusAbi.successStatus);
    await settleReporting();
    disposeNetWasmInterop({ preparation });
  }
});

test("rejects invalid interop lifetime actions before composition", () => {
  const options = {
    manifest: manifest(), runtimeModules: {}, parsedStackTraceSymbols: [],
  };
  assert.throws(() => prepareParsedNetWasmInterop({
    ...options, assertAvailable: null,
  }), /lifetime actions/);
  assert.throws(() => prepareParsedNetWasmInterop({
    ...options, assertAsyncDeliveryAvailable: null,
  }), /lifetime actions/);
  assert.throws(() => prepareParsedNetWasmInterop({
    ...options, observeAsyncFailure: null,
  }), /lifetime actions/);
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
    new Uint16Array(fixture.memory.buffer, 88, 2).set([97, 116]);
    fixture.report(2, 0, 0);
    fixture.report(2, 64, 3, 80, 2);
    await settleReporting();
    assert.equal(messages.length, 4);
    assert.match(messages[0], /<no stored message>/);
    assert.match(messages[1], /a \(message truncated by host policy\)/);
    assert.match(messages[1], /\nat$/);
    assert.match(messages[2], /unavailable/);
    assert.match(messages[3], /unavailable/);
    assert.match(messages[3], /\nat$/);
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
    report: raw.imports[builtinModule].report_terminal_exception_v2,
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
