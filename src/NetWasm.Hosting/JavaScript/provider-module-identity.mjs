// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: MIT

import { exactSemanticVersion } from "./exact-semantic-version.mjs";

const requestKeys = ["module"];
const modulePattern = new RegExp(
  `^([a-z0-9][a-z0-9.-]*:[a-z0-9][a-z0-9.-]*)/([a-z0-9][a-z0-9.-]*)@(${exactSemanticVersion})$`,
  "u");

export function projectProviderModuleIdentity(request) {
  assertExactDataObject(request, requestKeys, "provider module projection request");
  const match = modulePattern.exec(request.module);
  if (match === null) {
    throw new TypeError("provider module must use canonical versioned WIT interface syntax");
  }
  const [, packageName, interfaceName] = match;
  return Object.freeze({
    componentModule: `${packageName}/${interfaceName}`,
    rawModule: request.module,
  });
}

function assertExactDataObject(value, keys, label) {
  if (value === null || typeof value !== "object" || Array.isArray(value)
      || Object.getOwnPropertySymbols(value).length !== 0) {
    throw new TypeError(`${label} is invalid`);
  }
  const prototype = Object.getPrototypeOf(value);
  const descriptors = Object.getOwnPropertyDescriptors(value);
  const actualKeys = Object.keys(descriptors).sort();
  if (prototype !== null && prototype !== Object.prototype
      || actualKeys.length !== keys.length
      || actualKeys.some((key, index) => key !== keys[index])
      || Object.values(descriptors).some(descriptor => !descriptor.enumerable || !("value" in descriptor))) {
    throw new TypeError(`${label} shape is invalid`);
  }
}
