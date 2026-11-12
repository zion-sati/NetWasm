// The compiler's symbols sidecar format (issue 225), all little-endian: u32 magic "NWSY", u16 version 1, u16 kind
// (1 type-symbols, 2 method-symbols), the build ID as 32 UTF-16 characters, u32 entry count, u32 strings per entry
// (4 or 1), then per entry a u32 ID and per string a u32 character offset and an i32 length (-1 for none), then
// the UTF-16 characters.
const magic = 0x5953574E;
const headerSize = 80;
export const typeSymbolsKind = 1;
export const methodSymbolsKind = 2;

export function readSymbolSidecar(bytes, kind) {
  if (!(bytes instanceof Uint8Array)) throw new TypeError("a symbols sidecar must be bytes");
  const view = new DataView(bytes.buffer, bytes.byteOffset, bytes.byteLength);
  if (bytes.byteLength < headerSize || view.getUint32(0, true) !== magic) {
    throw new TypeError("not a NetWasm symbols sidecar");
  }
  if (view.getUint16(4, true) !== 1 || view.getUint16(6, true) !== kind) {
    throw new TypeError("another version or kind of NetWasm symbols sidecar");
  }
  const count = view.getInt32(72, true);
  const stringsPerEntry = view.getInt32(76, true);
  const entrySize = 4 + stringsPerEntry * 8;
  const characters = headerSize + count * entrySize;
  if (stringsPerEntry !== (kind === typeSymbolsKind ? 4 : 1) || count < 0
      || characters > bytes.byteLength || (bytes.byteLength - characters) % 2 !== 0) {
    throw new TypeError("the symbols sidecar's entries do not fit the file");
  }
  const characterCount = (bytes.byteLength - characters) / 2;
  const text = (offset, length) => {
    let value = "";
    for (let index = 0; index < length; index++) {
      value += String.fromCharCode(view.getUint16(characters + (offset + index) * 2, true));
    }
    return value;
  };
  const entries = [];
  let previous = -1;
  for (let entry = 0; entry < count; entry++) {
    const field = headerSize + entry * entrySize;
    const id = view.getInt32(field, true);
    if (id <= previous) throw new TypeError("the symbols sidecar's IDs are not in order");
    previous = id;
    const strings = [];
    for (let index = 0; index < stringsPerEntry; index++) {
      const offset = view.getInt32(field + 4 + index * 8, true);
      const length = view.getInt32(field + 8 + index * 8, true);
      if (length < -1 || (length >= 0 && (offset < 0 || offset + length > characterCount))) {
        throw new TypeError("a symbols sidecar string lies outside the file");
      }
      strings.push(length < 0 ? null : text(offset, length));
    }
    entries.push(Object.freeze({ id, strings: Object.freeze(strings) }));
  }
  return Object.freeze({ buildId: readBuildId(view), entries: Object.freeze(entries) });
}

function readBuildId(view) {
  let value = "";
  for (let index = 0; index < 32; index++) value += String.fromCharCode(view.getUint16(8 + index * 2, true));
  return value;
}

// Each type ID's names: name, namespace, full name and display text.
export function readTypeSymbols(bytes) {
  const sidecar = readSymbolSidecar(bytes, typeSymbolsKind);
  return Object.freeze({
    buildId: sidecar.buildId,
    types: new Map(sidecar.entries.map(({ id, strings: [name, namespace, fullName, displayName] }) =>
      [id, Object.freeze({ name, namespace, fullName, displayName })])),
  });
}

// Each stack-trace symbol's frame text.
export function readMethodSymbols(bytes) {
  return Object.freeze(readSymbolSidecar(bytes, methodSymbolsKind).entries
    .map(({ id, strings: [name] }) => Object.freeze({ id, name })));
}
