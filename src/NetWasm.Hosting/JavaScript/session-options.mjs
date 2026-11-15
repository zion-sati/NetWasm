// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: MIT

// Per-instance session options that page and worker hosts share: an
// environment merged over the build's, and guest output delivered as lines.

export function readEnvironmentOverride(value, label) {
  if (value === undefined) return undefined;
  if (value === null || typeof value !== "object" || Array.isArray(value)
      || Object.getOwnPropertySymbols(value).length !== 0
      || Object.getPrototypeOf(value) !== null && Object.getPrototypeOf(value) !== Object.prototype
      || Object.values(Object.getOwnPropertyDescriptors(value))
        .some(descriptor => !descriptor.enumerable || !("value" in descriptor))) {
    throw new TypeError(`${label} must be a plain object`);
  }
  if (Object.values(value).some(entry => typeof entry !== "string")) {
    throw new TypeError(`${label} values must be strings`);
  }
  return Object.freeze({ ...value });
}

// Override values win over the build's; the guest may read every name.
export function mergeEnvironment(configuration, environment) {
  if (environment === undefined) return configuration;
  const values = new Map(configuration.environment.map(variable => [variable.name, variable.value]));
  for (const [name, value] of Object.entries(environment)) values.set(name, value);
  const merged = Object.freeze([...values].map(([name, value]) => Object.freeze({ name, value })));
  return Object.freeze({
    ...configuration,
    environment: merged,
    grants: Object.freeze({
      ...configuration.grants,
      environment: Object.freeze(merged.map(variable => variable.name)),
    }),
  });
}

// Delivers guest output one line at a time; flush() delivers a final partial
// line when the session ends.
export function createLineOutput(emit) {
  if (typeof emit !== "function") throw new TypeError("line output requires a line action");
  const decoder = new TextDecoder();
  let pending = "";
  const deliver = text => {
    pending += text;
    for (let end = pending.indexOf("\n"); end !== -1; end = pending.indexOf("\n")) {
      const line = pending.slice(0, end);
      pending = pending.slice(end + 1);
      emit(line.endsWith("\r") ? line.slice(0, -1) : line);
    }
  };
  return Object.freeze({
    sink: Object.freeze({ write: bytes => deliver(decoder.decode(bytes, { stream: true })) }),
    flush() {
      deliver(decoder.decode());
      if (pending.length !== 0) {
        const line = pending;
        pending = "";
        emit(line);
      }
    },
  });
}

export function consoleLine(level) {
  return line => console[level](line);
}
