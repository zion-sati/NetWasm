// NetWasm Community License 1.0; see the repository source-path license map.
import { ToolLimitError } from './tool-inputs.mjs';
const pageSize = 65536;

export function validateWasm32MemoryCeiling(maximumMemoryBytes) {
  if (!Number.isSafeInteger(maximumMemoryBytes) || maximumMemoryBytes < pageSize ||
      maximumMemoryBytes > 4294967296 || maximumMemoryBytes % pageSize !== 0)
    throw Error('Tool memory maximum must be whole wasm32 pages between 64 KiB and 4 GiB');
  return maximumMemoryBytes / pageSize;
}

/** Own the result and change only the maximum of one defined, unshared wasm32
 * memory. Trusted CLI modules may import functions, but no other import kinds.
 * The caller verifies original asset bytes before applying this policy. */
export function capDefinedWasm32Memory(bytes, maximumMemoryBytes) {
  const ceiling = validateWasm32MemoryCeiling(maximumMemoryBytes);
  if (!(bytes instanceof Uint8Array) || bytes.length < 8 ||
      ![0, 97, 115, 109, 1, 0, 0, 0].every((value, index) => bytes[index] === value))
    throw Error('Tool must be a WebAssembly core version 1 module');
  const reader = new Reader(bytes, 8, bytes.length);
  let memory;
  while (reader.position < reader.end) {
    const sectionStart = reader.position, id = reader.byte(), length = reader.u32();
    const section = reader.slice(length);
    if (id === 2) {
      const count = section.u32();
      for (let index = 0; index < count; index++) {
        section.skipName(); section.skipName();
        if (section.byte() !== 0) throw Error('Memory-capped tools may import only functions');
        section.u32();
      }
      section.complete();
    }
    if (id !== 5) continue;
    if (memory || section.u32() !== 1) throw Error('Tool must define exactly one memory');
    const flags = section.u32();
    if (flags !== 0 && flags !== 1) throw Error('Tool memory must be unshared wasm32');
    const initial = section.u32(), maximum = flags === 1 ? section.u32() : null;
    section.complete();
    if (initial > 65536 || (maximum !== null && (maximum < initial || maximum > 65536)))
      throw Error('Invalid wasm32 memory limits');
    if (initial > ceiling) throw new ToolLimitError('Tool initial memory exceeds its maximum');
    memory = { sectionStart, sectionEnd: reader.position, initial,
      maximum: Math.min(maximum ?? ceiling, ceiling) };
  }
  if (!memory) throw Error('Tool must define exactly one memory');
  const payload = Uint8Array.of(1, 1, ...u32(memory.initial), ...u32(memory.maximum));
  const header = Uint8Array.of(5, ...u32(payload.length));
  const output = new Uint8Array(memory.sectionStart + header.length + payload.length + bytes.length - memory.sectionEnd);
  output.set(bytes.subarray(0, memory.sectionStart));
  output.set(header, memory.sectionStart);
  output.set(payload, memory.sectionStart + header.length);
  output.set(bytes.subarray(memory.sectionEnd), memory.sectionStart + header.length + payload.length);
  return output;
}

class Reader {
  constructor(bytes, position, end) { this.bytes = bytes; this.position = position; this.end = end; }
  byte() {
    if (this.position >= this.end) throw Error('Truncated tool module');
    return this.bytes[this.position++];
  }
  u32() {
    let value = 0;
    for (let index = 0; index < 5; index++) {
      const byte = this.byte();
      if (index === 4 && byte > 15) throw Error('Invalid tool module u32');
      value += (byte & 127) * 2 ** (index * 7);
      if (!(byte & 128)) return value;
    }
    throw Error('Invalid tool module u32');
  }
  slice(length) {
    if (length > this.end - this.position) throw Error('Truncated tool section');
    const result = new Reader(this.bytes, this.position, this.position + length);
    this.position += length;
    return result;
  }
  skipName() { this.slice(this.u32()); }
  complete() { if (this.position !== this.end) throw Error('Trailing tool section bytes'); }
}
function u32(value) {
  const bytes = [];
  do { const byte = value % 128; value = Math.floor(value / 128); bytes.push(byte | (value ? 128 : 0)); } while (value);
  return bytes;
}
