// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: MIT

import assert from "node:assert/strict";
import test from "node:test";

import { readTimeZoneNames } from "./timezone-sidecar-zones.mjs";

export function timeZoneContainer(zones, version = "2026c") {
  const parts = [Uint8Array.of(0x4e, 0x57, 0x54, 0x5a, 1, 0, 0, 0)];
  const text = value => {
    const bytes = new TextEncoder().encode(value);
    const header = new Uint8Array(2);
    new DataView(header.buffer).setUint16(0, bytes.length, true);
    parts.push(header, bytes);
  };
  const int32 = value => {
    const bytes = new Uint8Array(4);
    new DataView(bytes.buffer).setInt32(0, value, true);
    parts.push(bytes);
  };
  text(version);
  int32(zones.length);
  for (const [name, transitions] of zones) {
    text(name);
    int32(36_000);
    parts.push(Uint8Array.of(0));
    int32(transitions);
    parts.push(new Uint8Array(transitions * 13));
  }
  parts.push(new Uint8Array(32));
  const result = new Uint8Array(parts.reduce((length, part) => length + part.length, 0));
  let offset = 0;
  for (const part of parts) {
    result.set(part, offset);
    offset += part.length;
  }
  return result;
}

test("reads every zone name from a container", () => {
  const bytes = timeZoneContainer([["America/New_York", 3], ["Australia/Melbourne", 0]]);
  assert.deepEqual([...readTimeZoneNames(bytes)], ["America/New_York", "Australia/Melbourne"]);
  const offsetView = new Uint8Array(bytes.length + 5);
  offsetView.set(bytes, 5);
  assert.deepEqual([...readTimeZoneNames(offsetView.subarray(5))], ["America/New_York", "Australia/Melbourne"]);
});

test("rejects malformed containers", () => {
  const valid = timeZoneContainer([["Australia/Melbourne", 2]]);
  const badMagic = new Uint8Array(valid);
  badMagic[4] = 2;
  const negativeCount = new Uint8Array(valid);
  new DataView(negativeCount.buffer).setInt32(15, -1, true);
  const extraByte = new Uint8Array(valid.length + 1);
  extraByte.set(valid.subarray(0, valid.length - 32));
  for (const bytes of [
    new Uint8Array(10), badMagic, negativeCount, extraByte,
    valid.subarray(0, valid.length - 40), timeZoneContainer([]).subarray(0, 40),
  ]) {
    assert.throws(() => readTimeZoneNames(bytes), /timezone sidecar is malformed/);
  }
  assert.throws(() => readTimeZoneNames([1, 2]), /container bytes are invalid/);
});
