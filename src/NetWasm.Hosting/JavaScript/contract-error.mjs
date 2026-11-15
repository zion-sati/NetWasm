// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: MIT

const contractErrors = new WeakSet();

export function createContractError(
  message = "NetWasm execution contract validation failed.",
  cause) {
  const error = cause === undefined ? new Error(message) : new Error(message, { cause });
  contractErrors.add(error);
  return error;
}

// A contract error that wraps a caught value reports that value as its cause.
export function unwrapContractError(value) {
  let current = value;
  while (isContractError(current) && Object.hasOwn(current, "cause")) {
    current = current.cause;
  }
  return current;
}

export function isContractError(value) {
  return value !== null && typeof value === "object" && contractErrors.has(value);
}
