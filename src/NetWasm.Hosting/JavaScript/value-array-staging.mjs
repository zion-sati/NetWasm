// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: MIT

import { describeValueArray, toValueArray } from "./interop-abi-types.mjs";
import { NetWasmHostError } from "./managed-errors.mjs";

const optionKeys = ["getInstance", "getMemory", "runtimeModules", "target"];

// A number array that enters managed code (an export's or callback's argument, an import's result) is copied into a
// block of linear memory from the runtime's allocator and passed as the block's address and the array's length,
// -1 for null and no block for an empty array. The managed code that takes it builds its array from the block and
// frees it, so no handle and no call back into JavaScript is needed. A block that never reaches managed code, because
// the call failed before entering it, is discarded.
export function createValueArrayStaging(options) {
  assertExactDataObject(options, optionKeys, "value-array staging options");
  if (typeof options.getInstance !== "function" || typeof options.getMemory !== "function") {
    throw new TypeError("value-array staging needs the instance and its memory");
  }
  if (options.target !== "wasm32" && options.target !== "wasm64") {
    throw new TypeError("value-array staging target is unsupported");
  }
  const wide = options.target === "wasm64";
  const none = wide ? 0n : 0;
  const allocator = name => {
    const runtime = options.runtimeModules["netwasm.runtime.v1"] ?? options.getInstance().exports;
    const action = runtime?.[name];
    if (typeof action !== "function") throw new NetWasmHostError(`the NetWasm runtime does not export ${name}`);
    return action;
  };

  return Object.freeze({
    // The two Wasm values an array crosses as: its block's address and its length.
    stage(type, value) {
      if (value === null || value === undefined) return [none, -1];
      const array = toValueArray(type, value);
      if (array === null) throw new NetWasmHostError(describeValueArray(type));
      if (array.length === 0) return [none, 0];
      const address = allocator("native_alloc")(wide ? BigInt(array.byteLength) : array.byteLength);
      if (address === none) throw new NetWasmHostError("the NetWasm runtime could not allocate a number array's elements");
      // The allocation may grow the memory, so the view is taken after it.
      new Uint8Array(options.getMemory().buffer, Number(address), array.byteLength)
        .set(new Uint8Array(array.buffer, array.byteOffset, array.byteLength));
      return [address, array.length];
    },
    discard(address) {
      if (address !== none) allocator("native_free")(address);
    },
  });
}

function assertExactDataObject(value, keys, label) {
  if (value === null || typeof value !== "object" || Array.isArray(value)) throw new TypeError(`${label} are invalid`);
  const actual = Object.keys(value).sort();
  if (actual.length !== keys.length || actual.some((key, index) => key !== keys[index])) {
    throw new TypeError(`${label} shape is invalid`);
  }
}
