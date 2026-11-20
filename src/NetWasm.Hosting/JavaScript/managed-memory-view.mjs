// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: MIT

import { memoryViewConstructor } from "./interop-abi-types.mjs";
import { NetWasmHostError } from "./managed-errors.mjs";

// The element types CoreLib's JSMarshal numbers a segment's kind by.
const kinds = Object.freeze([
  "u8-view", "i8-view", "i16-view", "u16-view", "i32-view", "u32-view", "i64-view", "u64-view", "f32-view", "f64-view",
]);

// .NET's IMemoryView over an ArraySegment C# passed to an import. It reads and writes the array's elements through
// linear memory as it is at each access, so the memory growing does not detach it, until JavaScript disposes it;
// release then lets the array go.
export class ManagedMemoryView {
  #readMemory;
  #address;
  #length;
  #arrayType;
  #release;

  constructor(readMemory, address, length, kind, release) {
    if (typeof readMemory !== "function" || typeof release !== "function"
        || !Number.isSafeInteger(length) || length < 0 || !Number.isInteger(kind) || kind < 0 || kind >= kinds.length) {
      throw new NetWasmHostError("a managed memory view is invalid");
    }
    this.#readMemory = readMemory;
    this.#address = Number(address);
    this.#length = length;
    this.#arrayType = memoryViewConstructor(kinds[kind]);
    this.#release = release;
  }

  get isDisposed() {
    return this.#release === null;
  }

  get length() {
    return this.#view().length;
  }

  get byteLength() {
    return this.#view().byteLength;
  }

  // Copies a typed array of the view's element type into the segment.
  set(source, targetOffset) {
    const target = this.#view();
    this.#requireElementType(source);
    target.set(source, targetOffset);
  }

  // Copies the segment, from sourceOffset on, into a typed array of its element type.
  copyTo(target, sourceOffset) {
    const source = this.#view();
    this.#requireElementType(target);
    target.set(source.subarray(sourceOffset));
  }

  // A copy of the segment or part of it.
  slice(start, end) {
    return this.#view().slice(start, end);
  }

  dispose() {
    const release = this.#release;
    if (release === null) return;
    this.#release = null;
    release();
  }

  #view() {
    if (this.#release === null) {
      throw new NetWasmHostError("the memory view is disposed");
    }
    return new this.#arrayType(this.#readMemory().buffer, this.#address, this.#length);
  }

  #requireElementType(array) {
    if (!(array instanceof this.#arrayType)) {
      throw new NetWasmHostError(`a memory view of ${this.#arrayType.name} takes a ${this.#arrayType.name}`);
    }
  }
}
