// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

// The C library's math functions for the simulated runtime. CoreLib's Math and MathF import them from libc, which
// every NetWasm program links; the oracle links no C library, so it instantiates musl's libm as Emscripten builds it
// (eng/build-oracle-libm.sh, the pinned Emscripten) and forwards each import to it. modf and modff return the
// integral part, and sincos and sincosf the sine and cosine, through pointers into the program's memory: the call
// gets cells in libm's own memory, and each result's bytes are copied across.
import { readFile } from "node:fs/promises";

// The functions that return results through pointers: how many results, and the size of each.
const pointerResults = new Map([["modf", [1, 8]], ["modff", [1, 4]], ["sincos", [2, 8]], ["sincosf", [2, 4]]]);

export async function loadOracleLibm(directory, target, programMemory) {
  if (target !== "wasm32" && target !== "wasm64") {
    throw new TypeError(`unknown oracle libm target '${target}'`);
  }
  if (typeof programMemory !== "function") {
    throw new TypeError("The program memory accessor must be a function.");
  }
  const manifest = JSON.parse(await readFile(new URL("oracle-libm.json", directory), "utf8"));
  const bytes = await readFile(new URL(`oracle-libm-${target}.wasm`, directory));
  const { instance } = await WebAssembly.instantiate(bytes, {});
  const libm = instance.exports;
  libm._initialize();
  const cell = libm.malloc(target === "wasm64" ? 16n : 16);
  const cellAt = offset => target === "wasm64" ? cell + BigInt(offset) : cell + offset;
  const functions = {};
  for (const name of manifest.functions) {
    const implementation = libm[name];
    if (typeof implementation !== "function") {
      throw new Error(`oracle libm does not export '${name}'`);
    }
    const results = pointerResults.get(name);
    functions[name] = results === undefined
      ? implementation
      : (value, ...pointers) => {
        const [count, size] = results;
        const cells = Array.from({ length: count }, (_, index) => cellAt(index * size));
        const result = implementation(value, ...cells);
        pointers.forEach((pointer, index) => new Uint8Array(programMemory().buffer, Number(pointer), size)
          .set(new Uint8Array(libm.memory.buffer, Number(cells[index]), size)));
        return result;
      };
  }
  return functions;
}
