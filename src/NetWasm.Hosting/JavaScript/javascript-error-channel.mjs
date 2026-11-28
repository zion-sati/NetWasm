// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: MIT

// The JavaScript error a failed import threw or rejected with, held for the JSException that managed code raises for
// the failure. As .NET's browser runtime marshals it (marshal_exception_to_cs), the exception's message is the value's
// toString(), read when the import fails, and the exception keeps the value itself, which it gives back when it crosses
// to JavaScript again. Managed code takes the error at once, before anything else can fail.
export function createJavaScriptErrorChannel(handles) {
  if (handles === null || typeof handles !== "object" || typeof handles.acquire !== "function") {
    throw new TypeError("JavaScript error channel handles are invalid");
  }
  let pending = null;
  // The JavaScript error an async export's Promise rejects with, which its managed exception held.
  let exportFailure = null;
  return Object.freeze({
    // A synchronous throw. .NET's runtime raises no exception for a thrown null or undefined, and the call returns as
    // if it succeeded; NetWasm raises one, as .NET does for a rejection without a reason.
    captureThrow(value) {
      pending = describe(value ?? new Error());
    },
    // A rejection. As .NET's PromiseHolder.reject, a reason that is falsy becomes new Error().
    captureRejection(reason) {
      pending = describe(reason || new Error());
    },
    // Drops an error nothing took: a callback-lowered Promise's failure callback that did not raise a JSException.
    discardPending() {
      pending = null;
    },
    // The pending value itself, or undefined when none is pending.
    pendingValue: () => pending?.value,
    // Whether an export's catch has offered a JavaScript error the host has not yet thrown.
    hasExportFailure: () => exportFailure !== null,
    // The JavaScript error the export that just completed offered, or null; the channel holds none after.
    takeExportFailure() {
      const offered = exportFailure;
      exportFailure = null;
      return offered;
    },
    services: Object.freeze({
      // The pending error's message, or null when no JavaScript error is pending.
      error_pending_message: () => pending === null ? null : pending.message,
      // The pending error's handle, which the caller owns; 0 when none is pending. The channel holds no error after.
      error_take() {
        if (pending === null) return 0;
        const { value } = pending;
        pending = null;
        return handles.acquire(value);
      },
      // The JavaScript stack of an error a JSException kept, or null where the value has none, which .NET's
      // JSException.StackTrace reads from the error's stack property.
      error_same: (first, second) => handles.get(first) === handles.get(second),
      error_offer_export(handle) {
        exportFailure = Object.freeze({ value: handles.get(handle) });
        handles.release(handle);
      },
      error_stack(handle) {
        const value = handles.get(handle);
        const stack = value !== null && typeof value === "object" ? value.stack : undefined;
        return typeof stack === "string" ? stack : null;
      },
    }),
  });
}

function describe(value) {
  try {
    return Object.freeze({ value, message: String(value.toString()) });
  } catch (cause) {
    // The value's own toString failed, so the import fails with that error instead, which describes itself.
    return Object.freeze({ value: cause, message: String(cause) });
  }
}
