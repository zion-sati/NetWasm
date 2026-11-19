// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: MIT

// A field value crosses the Fetch API as text, one Latin-1 character per byte, and reaches the
// guest as those bytes.
export const fieldValueText = bytes => String.fromCharCode(...bytes);

export const fieldValueBytes = text => Uint8Array.from(text, character => character.charCodeAt(0) & 0xff);
