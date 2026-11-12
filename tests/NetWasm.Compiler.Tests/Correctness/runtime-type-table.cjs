'use strict';
// The runtime type table (RuntimeTypeTableEncoder in the compiler), for the test hosts that stand in
// for the runtime: one bit stream, least significant bit first, every offset a bit offset from the
// table's start.

const FINALIZER = 2 ** 29;
const VALUE_LAYOUT = 2 ** 30;
const REGISTERED = 2 ** 31;
const INTERFACE = 4;

const reader = (bytes, table) => {
  const read = (offset, width) => {
    let value = 0;
    for (let bit = 0; bit < width; bit++) {
      const position = offset + bit;
      if ((bytes[table + Math.floor(position / 8)] >> (position % 8)) & 1) value += 2 ** bit;
    }
    return value;
  };
  return { read, header: field => read(field * 32, 32) };
};

// Every registered type, decoded once as the runtime does at startup.
const readRuntimeTypeTable = (bytes, table) => {
  const { read, header } = reader(bytes, table);
  const capacity = header(1);
  const widths = header(2);
  const idBits = widths & 63;
  const shapeIndexBits = (widths >>> 6) & 63;
  const flagIndexBits = (widths >>> 12) & 63;
  const layoutIndexBits = (widths >>> 18) & 63;
  const shapeBits = (widths >>> 24) & 63;
  const countBits = header(3) & 63;
  const sizeBits = header(11) & 63;
  const bitmapIndexBits = (header(11) >>> 6) & 63;
  const payloadBits = (header(11) >>> 12) & 63;
  const layoutBits = 2 * sizeBits + 2 * bitmapIndexBits + payloadBits;
  const poolCount = header(12);
  const poolOffsets = header(13);
  const poolOffsetBits = header(14) & 63;
  const poolLengthBits = (header(14) >>> 6) & 63;
  const hasReferences = index => {
    if (index === 0) return false;
    const entry = poolOffsets + poolCount * poolOffsetBits +
      read(poolOffsets + (index - 1) * poolOffsetBits, poolOffsetBits);
    const length = read(entry, poolLengthBits);
    for (let bit = 0; bit < length; bit++) {
      if (read(entry + poolLengthBits + bit, 1)) return true;
    }
    return false;
  };
  const explicitBases = header(15);
  const assignable = header(16);
  let explicitIds = explicitBases + capacity;
  let lists = assignable + capacity;
  const types = [];
  for (let type = 1; type < capacity; type++) {
    const shape = header(8) + read(header(6) + type * shapeIndexBits, shapeIndexBits) * shapeBits;
    const flagIndex = read(shape, flagIndexBits);
    let base = read(header(5) + flagIndex * idBits, idBits);
    if (read(explicitBases + type, 1)) {
      base = read(explicitIds, idBits);
      explicitIds += idBits;
    }
    const ids = new Set();
    if (read(assignable + type, 1)) {
      const count = read(lists, countBits);
      lists += countBits;
      for (let index = 0; index < count; index++, lists += idBits) ids.add(read(lists, idBits));
    }
    if (flagIndex === 0) continue;
    const flags = read(640 + flagIndex * 32, 32);
    const layout = header(10) + read(shape + flagIndexBits, layoutIndexBits) * layoutBits;
    types.push({
      type,
      base,
      size: read(layout, sizeBits),
      assignable: ids,
      isInterface: (flags & INTERFACE) !== 0,
      hasFinalizer: Math.floor(flags / FINALIZER) % 2 === 1,
      references: hasReferences(read(layout + sizeBits, bitmapIndexBits)),
      value: Math.floor(flags / VALUE_LAYOUT) % 2 === 1
        ? {
          size: read(layout + sizeBits + bitmapIndexBits, sizeBits),
          payload: read(layout + 2 * sizeBits + bitmapIndexBits, payloadBits),
          references: hasReferences(read(layout + 2 * sizeBits + bitmapIndexBits + payloadBits, bitmapIndexBits)),
        }
        : null,
    });
  }
  return types;
};

const width = maximum => maximum === 0 ? 0 : Math.floor(Math.log2(maximum)) + 1;

// A table for runtime contract tests, every field explicit: each type its own shape and layout,
// its base stated rather than defaulted, its whole assignable list. Each type is
// { type, base, size, references: [slot...], assignable: [id...], isInterface, hasFinalizer,
//   value: { size, payload, references: [slot...] } }.
const encodeRuntimeTypeTable = types => {
  const bits = [];
  const write = (value, count) => {
    for (let bit = 0; bit < count; bit++) bits.push(Math.floor(value / 2 ** bit) % 2);
  };
  const patch = (offset, value) => {
    for (let bit = 0; bit < 32; bit++) bits[offset + bit] = Math.floor(value / 2 ** bit) % 2;
  };
  const byId = new Map(types.map(type => [type.type, type]));
  const capacity = Math.max(0, ...types.map(type => type.type)) + 1;
  const word = type => REGISTERED + (type.hasFinalizer ? FINALIZER : 0) + (type.value ? VALUE_LAYOUT : 0) +
    (type.isInterface ? INTERFACE : 0);
  const words = [0, ...new Set(types.map(word))];
  const pool = [];
  const bitmap = slots => {
    if (!slots || slots.length === 0) return 0;
    pool.push(slots);
    return pool.length;
  };
  const layouts = types.map(type => [type.size, bitmap(type.references), type.value?.size ?? 0,
    type.value?.payload ?? 0, type.value ? bitmap(type.value.references) : 0]);
  const idBits = width(capacity - 1);
  const shapeIndexBits = width(types.length);
  const flagIndexBits = width(words.length - 1);
  const layoutIndexBits = width(Math.max(0, layouts.length - 1));
  const countBits = width(Math.max(0, ...types.map(type => (type.assignable ?? []).length)));
  const sizeBits = width(Math.max(0, ...layouts.map(layout => Math.max(layout[0], layout[2]))));
  const bitmapIndexBits = width(pool.length);
  const payloadBits = width(Math.max(0, ...layouts.map(layout => layout[3])));
  const lengths = pool.map(slots => Math.max(...slots) + 1);
  const lengthBits = width(Math.max(0, ...lengths));
  const poolBits = lengths.reduce((total, length) => total + lengthBits + length, 0);
  const poolOffsetBits = width(poolBits);
  for (let field = 0; field < 20; field++) write(0, 32);
  patch(0, 1);
  patch(32, capacity);
  patch(64, idBits + shapeIndexBits * 64 + flagIndexBits * 4096 + layoutIndexBits * 262144 +
    (flagIndexBits + layoutIndexBits) * 16777216);
  patch(96, countBits);
  patch(128, words.length);
  words.forEach(value => write(value, 32));
  patch(160, bits.length);
  words.forEach(() => write(0, idBits));
  // Type index i (from 0) has shape i + 1, whose layout is layouts[i]; shape 0 marks unused IDs.
  patch(192, bits.length);
  for (let id = 0; id < capacity; id++) {
    const index = types.findIndex(type => type.type === id);
    write(index < 0 ? 0 : index + 1, shapeIndexBits);
  }
  patch(256, bits.length);
  write(0, flagIndexBits + layoutIndexBits);
  types.forEach((type, index) => {
    write(words.indexOf(word(type)), flagIndexBits);
    write(index, layoutIndexBits);
  });
  patch(288, layouts.length);
  patch(320, bits.length);
  patch(352, sizeBits + bitmapIndexBits * 64 + payloadBits * 4096);
  for (const [size, objectBitmap, valueSize, payload, valueBitmap] of layouts) {
    write(size, sizeBits);
    write(objectBitmap, bitmapIndexBits);
    write(valueSize, sizeBits);
    write(payload, payloadBits);
    write(valueBitmap, bitmapIndexBits);
  }
  patch(384, pool.length);
  patch(416, bits.length);
  patch(448, poolOffsetBits + lengthBits * 64);
  let offset = 0;
  lengths.forEach(length => { write(offset, poolOffsetBits); offset += lengthBits + length; });
  pool.forEach((slots, index) => {
    write(lengths[index], lengthBits);
    for (let slot = 0; slot < lengths[index]; slot++) write(slots.includes(slot) ? 1 : 0, 1);
  });
  patch(480, bits.length);
  for (let id = 0; id < capacity; id++) write(byId.get(id)?.base ? 1 : 0, 1);
  for (let id = 0; id < capacity; id++) if (byId.get(id)?.base) write(byId.get(id).base, idBits);
  patch(512, bits.length);
  for (let id = 0; id < capacity; id++) write((byId.get(id)?.assignable ?? []).length ? 1 : 0, 1);
  for (let id = 0; id < capacity; id++) {
    const list = byId.get(id)?.assignable ?? [];
    if (list.length === 0) continue;
    write(list.length, countBits);
    list.forEach(item => write(item, idBits));
  }
  const bytes = new Uint8Array(Math.ceil(bits.length / 8) + 8);
  bits.forEach((bit, index) => { if (bit) bytes[index >> 3] |= 1 << (index & 7); });
  return bytes;
};

// Registers types with a runtime the way a module's initializer does: one table, written into
// memory the runtime allocates, and one register_types call.
const registerRuntimeTypes = (runtime, types, address = value => value) => {
  const bytes = encodeRuntimeTypeTable(types);
  const table = runtime.native_alloc(address(bytes.length));
  new Uint8Array(runtime.memory.buffer).set(bytes, Number(table));
  runtime.register_types(table);
};

module.exports = { readRuntimeTypeTable, encodeRuntimeTypeTable, registerRuntimeTypes };
