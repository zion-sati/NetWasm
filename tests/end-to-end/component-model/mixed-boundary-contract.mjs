import { createRuntimeContractImports } from "../../NetWasm.Runtime.Tests/runtime-contract-imports.mjs";
import { createYieldReactorImports } from "../../NetWasm.Runtime.Tests/yield-reactor-imports.mjs";

export async function runMixedBoundary(
  instantiateNetWasm,
  module,
  manifest,
  target) {
  if (target !== manifest.target) throw new Error("Host target must match the manifest");
  module = await WebAssembly.compile(module);
  if (WebAssembly.Module.imports(module).some(({ module }) =>
    module === "wasi_snapshot_preview1" || module === "wasi_unstable"))
    throw new Error("Preview 1 imports are forbidden");
  var equal = (actual, expected, message) => {
    if (actual !== expected) {
      throw new Error(`${message}: ${String(actual)} !== ${String(expected)}`);
    }
  };
  var invoke = (name, operation) => {
    try {
      return operation();
    } catch (error) {
      throw new Error(`${name} failed`, { cause: error });
    }
  };
  var address = value => typeof value === "bigint" ? Number(value) : value;
  var clockCalls = 0;
  var arithmeticCalls = 0;
  var multiplyCalls = 0;
  var canonicalPrefix = target === "wasm64" ? "cm64p2" : "cm32p2";
  var managed;
  var wakeReactor = token => managed.instance.exports[
    `${canonicalPrefix}|netwasm:runtime/reactor-guest@1|wake`](token);
  var getMemory = () => managed.instance.exports.memory;
  var stoppedMediaStreams = 0;
  managed = await instantiateNetWasm({
    module,
    manifest,
    runtimeModules: {
      ...createRuntimeContractImports(target, getMemory),
      ...createYieldReactorImports(target, queueMicrotask, wakeReactor),
      "netwasm.host.v1": { write_i32() {} },
      [`${target === "wasm64" ? "cm64p2" : "cm32p2"}|netwasm:mixed/arithmetic@1`]: {
        add: (left, right) => {
          arithmeticCalls++;
          return left + right;
        },
      },
      [`${target === "wasm64" ? "cm64p2" : "cm32p2"}|wasi:clocks/wall-clock@0.2`]: {
        now(result) {
          clockCalls++;
          var view = new DataView(getMemory().buffer);
          var base = address(result);
          view.setBigUint64(base, 1n, true);
          view.setUint32(base + 8, 2, true);
        },
      },
    },
    consumerModules: {
      "consumer.mixed": {
        multiply: (left, right) => {
          multiplyCalls++;
          return left * right;
        },
        increment_async: value => Promise.resolve(value + 1),
        open_media_stream: () => ({ tracks: 2, stopped: false }),
        media_track_count: stream => stream.tracks,
        stop_media_stream: stream => {
          stream.stopped = true;
          stoppedMediaStreams++;
        },
      },
    },
  });

  equal(invoke("mixed_sync", () => managed.exports.mixed_sync(20)), 42,
    "direct JS and WIT/WASI sync composition");
  equal(await managed.exports.mixed_async(40), 42, "direct JS Promise composition");
  equal(
    invoke("canonical run", () =>
      managed.instance.exports[`${canonicalPrefix}|netwasm:mixed/acceptance@1|run`](20)),
    42,
    "canonical export composition");
  var activeMemory = getMemory();
  var viewBeforeCollection = new DataView(activeMemory.buffer);
  var readReference = view => target === "wasm64"
    ? view.getBigUint64(16, true) : view.getUint32(16, true);
  var clockBeforeCollection = readReference(viewBeforeCollection);
  var clockTypeBeforeCollection = viewBeforeCollection.getUint32(address(clockBeforeCollection), true);
  managed.instance.exports.test_collect();
  var viewAfterCollection = new DataView(activeMemory.buffer);
  equal(readReference(viewAfterCollection), clockBeforeCollection, "clock static root");
  equal(viewAfterCollection.getUint32(address(clockBeforeCollection), true),
    clockTypeBeforeCollection, "clock object after collection");
  equal(invoke("post-collection mixed_sync", () => managed.exports.mixed_sync(20)), 42,
    "post-collection re-entry");
  equal(invoke("media_resource", () => managed.exports.media_resource(40)), 42,
    "browser media resource");
  equal(stoppedMediaStreams, 1, "browser media resource disposal");
  try {
    equal(managed.exports.mixed_sync(20), 42, "post-media re-entry");
  } catch (error) {
    throw new Error(
      `post-media re-entry failed clocks=${clockCalls} arithmetic=${arithmeticCalls} ` +
      `multiply=${multiplyCalls} roots=${managed.instance.exports.test_shadow_depth?.()} ` +
      `eh=${managed.instance.exports.test_exception_frame_depth?.()} ` +
      `componentAllocations=${managed.instance.exports.test_component_active_allocation_count?.()}`,
      { cause: error });
  }
  equal(managed.instance.exports.test_handle_count(), 0, "managed handle cleanup");
  equal(managed.handles.count, 0, "JavaScript handle cleanup");
  managed.dispose();
}
