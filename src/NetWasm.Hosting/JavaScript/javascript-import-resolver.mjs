// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: MIT

// A [JSImport]'s function, found as .NET's browser runtime finds it: a dotted name ("AvaloniaDOM.addClass") is a path
// through its scope, which is the declared module, or globalThis for a name that starts with "globalThis" and names
// no module. The function runs with the object it was found on as `this`, as a method call would. Null when the path
// ends anywhere but at a function.
export function resolveJavaScriptImport(scope, name) {
  if (scope === null || (typeof scope !== "object" && typeof scope !== "function") || typeof name !== "string") {
    return null;
  }
  const parts = name.split(".");
  let owner = scope;
  for (const part of parts.slice(0, -1)) {
    owner = owner?.[part];
    if (owner === null || (typeof owner !== "object" && typeof owner !== "function")) {
      return null;
    }
  }
  const service = owner[parts[parts.length - 1]];
  return typeof service === "function" ? service.bind(owner) : null;
}

// The scope of a module-less import: globalThis, with the leading "globalThis." taken off the path.
export function resolveGlobalJavaScriptImport(name) {
  const prefix = "globalThis.";
  return typeof name === "string" && name.startsWith(prefix) && name.length > prefix.length
    ? resolveJavaScriptImport(globalThis, name.slice(prefix.length))
    : null;
}
