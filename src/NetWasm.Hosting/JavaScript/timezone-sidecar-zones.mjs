// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: MIT

// Reads the zone names from a NetWasm time zone container (written by
// NetWasm.TimeZones): magic, IANA version, zone count, then each zone's name,
// initial offset, daylight flag and transitions, then a SHA-256 trailer.
const magic = Object.freeze([0x4e, 0x57, 0x54, 0x5a, 1, 0, 0, 0]);
const transitionSize = 13;
const trailerSize = 32;
const decoder = new TextDecoder("ascii");

export function readTimeZoneNames(bytes) {
  if (!(bytes instanceof Uint8Array)) throw new TypeError("timezone container bytes are invalid");
  const view = new DataView(bytes.buffer, bytes.byteOffset, bytes.byteLength);
  const end = bytes.length - trailerSize;
  let offset = 0;
  const need = length => {
    if (length < 0 || offset + length > end) throw new TypeError("timezone sidecar is malformed");
  };
  const text = () => {
    need(2);
    const length = view.getUint16(offset, true);
    offset += 2;
    need(length);
    const value = decoder.decode(bytes.subarray(offset, offset + length));
    offset += length;
    return value;
  };
  need(magic.length);
  if (magic.some((value, index) => bytes[index] !== value)) {
    throw new TypeError("timezone sidecar is malformed");
  }
  offset = magic.length;
  text();
  need(4);
  const count = view.getInt32(offset, true);
  offset += 4;
  need(count);
  const names = new Set();
  for (let index = 0; index < count; index++) {
    names.add(text());
    need(9);
    const transitions = view.getInt32(offset + 5, true);
    offset += 9;
    need(transitions * transitionSize);
    offset += transitions * transitionSize;
  }
  if (offset !== end) throw new TypeError("timezone sidecar is malformed");
  return names;
}
