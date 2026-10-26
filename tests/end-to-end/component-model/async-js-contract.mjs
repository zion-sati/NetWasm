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
        resolve_text: async value => value === null ? null : `${value}!`,
        resolve_doubles: async values => values.length === 0
          ? null
          : Float64Array.from(values, value => value * 1.5),
        resolve_object: value => Promise.resolve(value < 0 ? null : { value: value * 10 }),
        object_value: host => host.value,
        reenter: depth => managed.exports.nest(depth),
      },
    },
  });

  equal(await managed.exports.round_trip_task(11), 22, "Task round trip");
  equal(await managed.exports.round_trip_value_task(11), 33, "ValueTask round trip");
  equal(await managed.exports.observe_rejection(11), 14, "managed rejection observation");
  equal(await managed.exports.immediate_value_task(11), 15, "immediate ValueTask");
  equal(await managed.exports.task_void(17), undefined, "void Task");
  equal(await managed.exports.value_task_void(18), undefined, "void ValueTask");
  // "tag!" is 4 characters; 2, 4 and 6 each times 1.5 sum to 18; the object carries 3 * 10.
  equal(await managed.exports.reference_round_trip("tag", Int32Array.of(2, 4, 6)), 4048,
    "string and array arguments with string, array, and JSObject async results");
  equal(await managed.exports.null_references(null), 15, "null async references");
  equal(await managed.exports.text_later("tag"), "tag!", "async string export result");
  const doubles = await managed.exports.doubles_later(Int32Array.of(2, 4));
  equal(doubles instanceof Float64Array ? [...doubles].join() : doubles, "3,6", "async array export result");
  equal(await managed.exports.doubles_later(new Int32Array(0)), null, "null async array export result");
  equal((await managed.exports.object_later(4))?.value, 40, "async JSObject export result");
  equal(await managed.exports.object_later(-1), null, "null async JSObject export result");
  var page = { value: 41 };
  equal(managed.exports.echo_object(page), page, "JSObject export identity");
  equal(managed.exports.echo_object(null), null, "null JSObject export");
  equal(managed.exports.read_object(page), 41, "JSObject export argument");
  equal(await managed.exports.read_object_later(page), 42, "async JSObject export argument");
  // 32 nested export calls are allowed; the 33rd is refused and the managed
  // caller observes it as a JSException. The session stays usable afterwards.
  equal(managed.exports.nest(31), 1024, "re-entrant exports at the depth limit");
  equal(managed.exports.nest(32), -1, "re-entrant exports beyond the depth limit");
  equal(managed.exports.nest(3), 16, "re-entrant exports after refusal");
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
  // Closing the session cancels pending imports and rejects pending exports.
  await rejects(
    () => canceledByDispose,
    error => error?.message === "NetWasm export session is closed",
    "adapter-disposal rejection");
  equal(managed.instance.exports.test_handle_count(), 0, "managed handles");
  equal(managed.handles.count, 0, "JavaScript handles");
}
