// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: MIT

export function validateRuntimeComponentMetadata(sections, expectedMetadata) {
  if (sections.length !== 1) {
    throw new Error('Runtime must contain exactly one native component metadata section');
  }
  if (expectedMetadata.byteLength === 0 || !Buffer.from(sections[0]).equals(expectedMetadata)) {
    throw new Error('Runtime component metadata does not match the native WIT contract');
  }
}
