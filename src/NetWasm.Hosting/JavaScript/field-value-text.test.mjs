// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: MIT

import assert from "node:assert/strict";
import test from "node:test";

import { fieldValueBytes, fieldValueText } from "./field-value-text.mjs";

test("every byte crosses as one Latin-1 character and back", () => {
  const bytes = Uint8Array.from({ length: 256 }, (_, index) => index);
  const text = fieldValueText(bytes);
  assert.equal(text.length, 256);
  assert.equal(text.at(-1), "ÿ");
  assert.deepEqual(fieldValueBytes(text), bytes);
});

test("a character beyond Latin-1 keeps its low byte", () => {
  assert.deepEqual(fieldValueBytes("café€"), Uint8Array.of(0x63, 0x61, 0x66, 0xe9, 0xac));
});
