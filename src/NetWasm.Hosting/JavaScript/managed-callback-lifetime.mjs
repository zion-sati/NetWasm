// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: MIT

import { NetWasmHostError } from "./managed-errors.mjs";

// A callback passed to an import that returns no JSSubscription belongs to JavaScript, as a delegate passed through
// .NET's browser runtime does: its managed handle lives until JavaScript collects the function the import received,
// or ends at once when the import fails. Without a FinalizationRegistry the handle lives as long as the program.
export function createManagedCallbackLifetime(release, Registry = globalThis.FinalizationRegistry) {
  if (typeof release !== "function") {
    throw new TypeError("managed callback release is invalid");
  }
  // A collected function's program may have ended; its handle then has nothing to release.
  const releaseQuietly = handle => {
    try { release(handle); } catch { }
  };
  const registry = typeof Registry === "function" ? new Registry(releaseQuietly) : null;
  return Object.freeze({
    // The function JavaScript receives for one callback. keep() hands its handle to the registry once the import
    // succeeds; drop() stops it calling a handle its failed import released.
    adopt(handle, callback) {
      let live = true;
      const adopted = Object.freeze((...arguments_) => {
        if (!live) {
          throw new NetWasmHostError("a managed callback was released when the import that passed it failed");
        }
        return callback(...arguments_);
      });
      return Object.freeze({
        function: adopted,
        keep() { registry?.register(adopted, handle); },
        drop() { live = false; },
      });
    },
    release: releaseQuietly,
  });
}
