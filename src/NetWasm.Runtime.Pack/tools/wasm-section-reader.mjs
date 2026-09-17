const decoder = new TextDecoder('utf-8', { fatal: true });

class Reader {
  constructor(bytes) {
    this.bytes = bytes;
    this.offset = 0;
  }

  readByte() {
    if (this.offset >= this.bytes.length) throw new Error('Unexpected end of WebAssembly binary');
    return this.bytes[this.offset++];
  }

  readU32() {
    let value = 0;
    for (let shift = 0; shift < 35; shift += 7) {
      const byte = this.readByte();
      value |= (byte & 0x7f) << shift;
      if ((byte & 0x80) === 0) return value >>> 0;
    }
    throw new Error('Invalid WebAssembly u32 encoding');
  }

  readBytes(length) {
    const end = this.offset + length;
    if (end > this.bytes.length) throw new Error('Unexpected end of WebAssembly binary');
    const value = this.bytes.subarray(this.offset, end);
    this.offset = end;
    return value;
  }

  readName() {
    return decoder.decode(this.readBytes(this.readU32()));
  }
}

function moduleSections(input) {
  const bytes = input instanceof Uint8Array ? input : new Uint8Array(input);
  const reader = new Reader(bytes);
  const header = reader.readBytes(8);
  if (!header.every((value, index) => value === [0, 97, 115, 109, 1, 0, 0, 0][index])) {
    throw new Error('Invalid WebAssembly module header');
  }
  const sections = [];
  while (reader.offset < bytes.length) {
    const id = reader.readByte();
    const payload = reader.readBytes(reader.readU32());
    sections.push({ id, payload });
  }
  return sections;
}

export function readFunctionImports(input) {
  const section = moduleSections(input).find(({ id }) => id === 2);
  if (section === undefined) return [];
  const reader = new Reader(section.payload);
  const imports = [];
  const count = reader.readU32();
  for (let index = 0; index < count; index++) {
    const module = reader.readName();
    const name = reader.readName();
    const kind = reader.readByte();
    if (kind !== 0) {
      imports.push({ module, name, kind: ['function', 'table', 'memory', 'global', 'tag'][kind] ?? 'unknown' });
      break;
    }
    reader.readU32();
    imports.push({ module, name, kind: 'function' });
  }
  return imports;
}

export function readCustomSections(input, expectedName) {
  const matches = [];
  for (const { id, payload } of moduleSections(input)) {
    if (id !== 0) continue;
    const reader = new Reader(payload);
    if (reader.readName() === expectedName) {
      matches.push(reader.readBytes(payload.length - reader.offset));
    }
  }
  return matches;
}
