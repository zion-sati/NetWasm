// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: MIT

// A host that knows when it is idle lets its module's collector use that time: once the module has run, the
// host asks for the next idle period and then calls the runtime's idle entry, which collects when the heap has
// grown enough for a collection to be worth its while. The collection the program was heading for then does
// not happen during its next call. Requests made before the idle period arrives are one request.
export function createIdleCollection(options) {
  if (options === null || typeof options !== "object"
      || typeof options.collect !== "function" || typeof options.requestIdle !== "function") {
    throw new TypeError("idle collection requires a collect action and an idle scheduler");
  }
  const { collect, requestIdle } = options;
  let requested = false;
  const run = () => {
    requested = false;
    collect();
  };
  return Object.freeze({
    request() {
      if (requested) return;
      requested = true;
      requestIdle(run);
    },
  });
}
