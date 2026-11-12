import assert from "node:assert/strict";
import test from "node:test";

import { readMethodSymbols, readSymbolSidecar, readTypeSymbols } from "./symbol-sidecar-reader.mjs";

// The compiler's symbols sidecar format (SymbolSidecarWriter) without string sharing, for host fixtures.
// Each entry is { id, strings }; kind 1 holds four strings per entry, kind 2 one.
export function symbolSidecar(kind, entries, {
  buildId = "b".repeat(32),
  magic = 0x5953574E,
  version = 1,
  stringsPerEntry = kind === 1 ? 4 : 1,
} = {}) {
  const entrySize = 4 + stringsPerEntry * 8;
  const header = 80 + entries.length * entrySize;
  const texts = entries.flatMap(entry => entry.strings);
  const bytes = new Uint8Array(header + texts.reduce((total, text) => total + (text?.length ?? 0), 0) * 2);
  const view = new DataView(bytes.buffer);
  view.setUint32(0, magic, true);
  view.setUint16(4, version, true);
  view.setUint16(6, kind, true);
  for (let index = 0; index < 32; index++) view.setUint16(8 + index * 2, buildId.charCodeAt(index), true);
  view.setInt32(72, entries.length, true);
  view.setInt32(76, stringsPerEntry, true);
  let offset = 0;
  entries.forEach((entry, index) => {
    const field = 80 + index * entrySize;
    view.setInt32(field, entry.id, true);
    entry.strings.forEach((text, string) => {
      view.setInt32(field + 4 + string * 8, offset, true);
      view.setInt32(field + 8 + string * 8, text?.length ?? -1, true);
      for (let character = 0; character < (text?.length ?? 0); character++) {
        view.setUint16(header + (offset + character) * 2, text.charCodeAt(character), true);
      }
      offset += text?.length ?? 0;
    });
  });
  return bytes;
}

// A type's four names as the compiler writes them: name, namespace, full name and display text.
export function typeSymbol(id, fullName) {
  const separator = fullName.lastIndexOf(".");
  return {
    id,
    strings: [fullName.slice(separator + 1), separator < 0 ? null : fullName.slice(0, separator), fullName, fullName],
  };
}

// The exact bytes SymbolSidecarWriterTests.WritesTheGoldenMethodSidecar pins: method 1 "Hi", build ID "a" x 32.
const golden = Uint8Array.from(Buffer.from(
  "4E5753590100020061006100610061006100610061006100610061006100610061006100610061006100610061006100"
  + "610061006100610061006100610061006100610061006100010000000100000001000000000000000200000048006900",
  "hex"));

test("reads the bytes the compiler writes", () => {
  assert.deepEqual(readSymbolSidecar(golden, 2), {
    buildId: "a".repeat(32),
    entries: [{ id: 1, strings: ["Hi"] }],
  });
  assert.deepEqual(readMethodSymbols(golden), [{ id: 1, name: "Hi" }]);
});

test("reads type names with absent namespaces and shared strings", () => {
  const bytes = symbolSidecar(1, [typeSymbol(3, "Global"), typeSymbol(9, "Shop.Order")], { buildId: "c".repeat(32) });
  const symbols = readTypeSymbols(bytes);

  assert.equal(symbols.buildId, "c".repeat(32));
  assert.deepEqual([...symbols.types], [
    [3, { name: "Global", namespace: null, fullName: "Global", displayName: "Global" }],
    [9, { name: "Order", namespace: "Shop", fullName: "Shop.Order", displayName: "Shop.Order" }],
  ]);
  assert.equal(Object.isFrozen(symbols.types.get(9)), true);

  // The writer stores each distinct string once; every entry may point at the same characters.
  const shared = symbolSidecar(2, [{ id: 1, strings: ["Run"] }, { id: 2, strings: [""] }]);
  new DataView(shared.buffer).setInt32(80 + 12 + 4, 0, true);
  new DataView(shared.buffer).setInt32(80 + 12 + 8, 3, true);
  assert.deepEqual(readMethodSymbols(shared), [{ id: 1, name: "Run" }, { id: 2, name: "Run" }]);
  assert.deepEqual(readMethodSymbols(golden.subarray(0)), readMethodSymbols(new Uint8Array(golden)));
});

test("rejects what is not this kind and version of symbols sidecar", () => {
  for (const value of [null, [1], golden.buffer]) {
    assert.throws(() => readSymbolSidecar(value, 2), /must be bytes/);
  }
  for (const bytes of [golden.subarray(0, 79), symbolSidecar(2, [], { magic: 0 })]) {
    assert.throws(() => readSymbolSidecar(bytes, 2), /not a NetWasm symbols sidecar/);
  }
  for (const bytes of [symbolSidecar(2, [], { version: 2 }), symbolSidecar(1, [])]) {
    assert.throws(() => readSymbolSidecar(bytes, 2), /another version or kind/);
  }
});

test("rejects entries and strings that do not fit the file", () => {
  const entries = [{ id: 1, strings: ["A"] }];
  const negativeCount = symbolSidecar(2, entries);
  new DataView(negativeCount.buffer).setInt32(72, -1, true);
  const oddCharacters = new Uint8Array(golden.length + 1);
  oddCharacters.set(golden);
  for (const bytes of [
    symbolSidecar(2, entries, { stringsPerEntry: 2 }),
    negativeCount,
    golden.subarray(0, 90),
    oddCharacters,
  ]) {
    assert.throws(() => readSymbolSidecar(bytes, 2), /entries do not fit the file/);
  }

  assert.throws(() => readSymbolSidecar(symbolSidecar(2, [{ id: 2, strings: ["B"] }, ...entries]), 2),
    /not in order/);
  assert.throws(() => readSymbolSidecar(symbolSidecar(2, [...entries, ...entries]), 2), /not in order/);

  for (const [offset, length] of [[0, -2], [-1, 1], [1, 1], [0, 2]]) {
    const bytes = symbolSidecar(2, entries);
    const view = new DataView(bytes.buffer);
    view.setInt32(84, offset, true);
    view.setInt32(88, length, true);
    assert.throws(() => readSymbolSidecar(bytes, 2), /outside the file/);
  }
});
