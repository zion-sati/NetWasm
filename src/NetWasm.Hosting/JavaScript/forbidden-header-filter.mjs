// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: MIT

// The field names wasi:http's fields refuse: connection-level fields the platform's HTTP stack
// owns. preview3-shim's Node.js host forbids the same names, so a request behaves alike in both.
const forbiddenNames = new Set([
  "connection",
  "http2-settings",
  "host",
  "keep-alive",
  "proxy-authenticate",
  "proxy-authorization",
  "proxy-connection",
  "transfer-encoding",
  "upgrade",
]);

export function isForbiddenFieldName(name) {
  return forbiddenNames.has(name.toLowerCase());
}

// A platform request's header entries without the forbidden ones, which its HTTP stack has
// already acted on (the authority reaches the guest as the request's own).
export function withoutForbiddenFields(entries) {
  return entries.filter(([name]) => !isForbiddenFieldName(name));
}
