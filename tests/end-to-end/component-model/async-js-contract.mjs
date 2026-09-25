import { createRuntimeContractImports } from "../../NetWasm.Runtime.Tests/runtime-contract-imports.mjs";
import { createYieldReactorImports } from "../../NetWasm.Runtime.Tests/yield-reactor-imports.mjs";
import { createOutputStreamImports } from "../../NetWasm.Runtime.Tests/output-stream-imports.mjs";

export async function runAsyncJavaScriptInterop(
  instantiateNetWasm,
  NetWasmManagedError,
  module,
  manifest) {
  module = await WebAssembly.compile(module);
  if (WebAssembly.Module.imports(module).some(({ module }) =>
    module === "wasi_snapshot_preview1" || module === "wasi_unstable"))
    throw new Error("Preview 1 imports are forbidden");
  var equal = (actual, expected, message) => {
    if (actual !== expected) {
      throw new Error(`${message}: ${String(actual)} !== ${String(expected)}`);
    }
  };
  var rejects = async (operation, predicate, message) => {
    try {
      await operation();
    } catch (error) {
      if (predicate(error)) return;
      throw new Error(`${message}: unexpected ${error?.name ?? typeof error}`);
    }
    throw new Error(`${message}: operation completed successfully`);
  };
  var canonicalPrefix = manifest.target === "wasm64" ? "cm64p2" : "cm32p2";
  var managed;
  var wakeReactor = token => managed.instance.exports[
    `${canonicalPrefix}|netwasm:runtime/reactor-guest@1|wake`](token);
  managed = await instantiateNetWasm({
    module,
    manifest,
    runtimeModules: {
      ...createRuntimeContractImports(manifest.target, () => managed.instance.exports.memory),
      ...createOutputStreamImports(manifest.target, () => managed.instance.exports.memory,
        () => { throw new Error("Unexpected async interop stdout"); },
        bytes => console.error(new TextDecoder().decode(bytes).trimEnd())),
      ...createYieldReactorImports(manifest.target, queueMicrotask, wakeReactor),
    },
    consumerModules: {
      "consumer.promises": {
        resolve: async value => {
          await Promise.resolve();
          return value + 10;
        },
        resolve_value: value => Promise.resolve(value + 20),
        reject: async () => { throw new Error("host rejection"); },
        never: () => new Promise(() => {}),
        signal: value => { equal(value, 17, "Task import argument"); },
        signal_value: async value => { equal(value, 18, "ValueTask import argument"); },
      },
    },
  });

  equal(await managed.exports.round_trip_task(11), 22, "Task round trip");
  equal(await managed.exports.round_trip_value_task(11), 33, "ValueTask round trip");
  equal(await managed.exports.observe_rejection(11), 14, "managed rejection observation");
  equal(await managed.exports.immediate_value_task(11), 15, "immediate ValueTask");
  equal(await managed.exports.task_void(17), undefined, "void Task");
  equal(await managed.exports.value_task_void(18), undefined, "void ValueTask");
  await rejects(
    () => managed.exports.fault(11),
    error => error instanceof NetWasmManagedError,
    "managed async export fault");
  await rejects(
    () => managed.exports.cancel(11),
    error => error?.name === "AbortError",
    "managed async export cancellation");
  var canceledByDispose = managed.exports.observe_cancellation(11);
  await Promise.resolve();
  managed.dispose();
  equal(await canceledByDispose, 16, "adapter-disposal cancellation");
  equal(managed.instance.exports.test_handle_count(), 0, "managed handles");
  equal(managed.handles.count, 0, "JavaScript handles");
}
