import assert from "node:assert/strict";
import { createHash, randomBytes } from "node:crypto";
import test from "node:test";

import { sha256 } from "./sha256.mjs";

const hex = digest => Buffer.from(digest).toString("hex");
const expected = bytes => createHash("sha256").update(bytes).digest("hex");

// Issue #139: a page outside a secure context hashes its deployment in
// JavaScript, so the result must be SHA-256's for every input length,
// including the lengths around the 56-byte padding boundary.
test("matches SHA-256 for every input length across two blocks of padding", () => {
  for (let length = 0; length <= 200; length++) {
    const bytes = randomBytes(length);
    assert.equal(hex(sha256(bytes)), expected(bytes), `length ${length}`);
  }
  const large = randomBytes(1_000_003);
  assert.equal(hex(sha256(large)), expected(large));
  assert.equal(hex(sha256(new TextEncoder().encode("abc"))),
    "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad");
});

test("hashes an ArrayBuffer and views over part of a buffer", () => {
  const buffer = randomBytes(300).buffer;
  const digest = sha256(buffer);
  assert.ok(digest instanceof ArrayBuffer);
  assert.equal(digest.byteLength, 32);
  assert.equal(hex(digest), expected(new Uint8Array(buffer)));
  assert.equal(hex(sha256(new Uint8Array(buffer, 7, 100))), expected(new Uint8Array(buffer, 7, 100)));
  assert.equal(hex(sha256(new DataView(buffer, 13, 70))), expected(new Uint8Array(buffer, 13, 70)));
  for (const value of [null, "bytes", [1, 2, 3], 42]) {
    assert.throws(() => sha256(value), { name: "TypeError" });
  }
});
