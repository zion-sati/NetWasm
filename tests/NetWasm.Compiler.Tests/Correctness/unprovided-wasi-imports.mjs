// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

// A test host provides little of wasi:sockets, wasi:tls and wasi:filesystem, and a program may link more of
// them than it calls: ClientWebSocket reaches name lookup and SocketsHttpHandler reaches TLS, for example. So
// each function of those interfaces that the module imports and the host does not provide exists and fails if
// it is called. Nothing else is filled in: an import of any other interface that the host lacks still fails to
// link. Both corpus hosts, the oracle's and the linked runtime's, and the async acceptance host
// (tests/end-to-end/async-runtime) complete their imports this way.
const unprovidedInterfaces = /^(cm(32|64)p2\|)?wasi:(sockets|tls|filesystem)\//;

// declared is what WebAssembly.Module.imports gives for the module; imports is what the host provides, by
// module name; host names the host in the failure.
export function addUnprovidedWasiImports(declared, imports, host) {
  if (!Array.isArray(declared))
    throw new TypeError("The module's declared imports are required.");
  if (imports === null || typeof imports !== "object")
    throw new TypeError("The host's imports are required.");
  if (typeof host !== "string" || host.length === 0)
    throw new TypeError("The host's name is required.");
  const completed = { ...imports };
  for (const { module, name, kind } of declared) {
    if (kind !== "function" || !unprovidedInterfaces.test(module)) continue;
    if (completed[module]?.[name] !== undefined) continue;
    completed[module] = { ...completed[module] };
    completed[module][name] = () => {
      throw new Error(`${host} does not provide ${module} ${name}`);
    };
  }
  return completed;
}
