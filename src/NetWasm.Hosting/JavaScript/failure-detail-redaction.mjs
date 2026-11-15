// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: MIT

import { mapFailureDetails } from "./execution-result.mjs";

const quotedSpanPattern = /(['"`])([^'"`]+)\1/gu;
const tokenPattern = /[^\s'"`()[\]{}<>,;]+/gu;
const trailingPunctuationPattern = /[.:!?]+$/u;
const hostPathPlaceholder = "<host-path>";

// Replaces each local path in failure details with a placeholder. The host
// decides what a local path is; a page host never redacts its own URLs.
export function createFailureDetailRedaction({ isAbsoluteHostPath }) {
  if (typeof isAbsoluteHostPath !== "function") {
    throw new TypeError("failure detail redaction requires an absolute host path test");
  }
  const isHostPath = text => text.length > 1
    && (text.startsWith("file:") || isAbsoluteHostPath(text));
  const redact = detail => detail
    .replace(quotedSpanPattern, (span, quote, text) =>
      isHostPath(text) ? `${quote}${hostPathPlaceholder}${quote}` : span)
    .replace(tokenPattern, token => {
      const suffix = token.match(trailingPunctuationPattern)?.[0] ?? "";
      const core = token.slice(0, token.length - suffix.length);
      return isHostPath(core) ? `${hostPathPlaceholder}${suffix}` : token;
    });
  return Object.freeze(result => mapFailureDetails(result, redact));
}
