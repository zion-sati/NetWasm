import { NetWasmHostError, NetWasmManagedError } from "./managed-errors.mjs";

export function observeManagedAsyncExport({
  name,
  readStatus,
  readResult,
  complete,
  schedule = globalThis.setTimeout.bind(globalThis),
  cancelScheduled = globalThis.clearTimeout.bind(globalThis),
  registerAbort = () => () => {},
}) {
  if (typeof name !== "string" || name.length === 0
      || [readStatus, readResult, complete, schedule, cancelScheduled, registerAbort]
        .some(action => typeof action !== "function")) {
    throw new TypeError("managed async export observation actions are invalid");
  }

  return new Promise((resolve, reject) => {
    let settled = false;
    let scheduled = false;
    let timer;
    let unregister = () => {};
    let registered = false;
    let abortRequested = false;
    let abortReason;

    async function finish(success, value, captureFailure = false) {
      if (settled) return;
      settled = true;
      const failures = [];
      let completion;
      const releaseActions = [
        ...(scheduled ? [() => cancelScheduled(timer)] : []),
        unregister,
        () => { completion = complete(); },
      ];
      for (const release of releaseActions) {
        try { release(); } catch (cause) { failures.push(cause); }
      }
      try {
        const managed = await completion;
        if (captureFailure && managed != null) value = new NetWasmManagedError(name, { managed });
      } catch (cause) {
        failures.push(cause);
      }
      if (failures.length !== 0) {
        if (!success) failures.unshift(value);
        reject(new NetWasmManagedError(name, {
          cause: new AggregateError(failures, "managed async export cleanup failed"),
        }));
      } else if (success) {
        resolve(value);
      } else {
        reject(value);
      }
    }

    function poll() {
      if (settled) return;
      scheduled = false;
      try {
        const status = readStatus();
        if (status === 0) {
          timer = schedule(poll, 0);
          scheduled = true;
        } else if (status === 1) {
          finish(true, readResult());
        } else if (status === 2) {
          finish(false, new NetWasmManagedError(name), true);
        } else if (status === 3) {
          finish(false, new DOMException("managed operation was canceled", "AbortError"));
        } else {
          finish(false, new NetWasmHostError("managed async export status is invalid"));
        }
      } catch (cause) {
        finish(false, cause instanceof WebAssembly.RuntimeError
          ? cause
          : new NetWasmManagedError(name, { cause }));
      }
    }

    try {
      const releaseRegistration = registerAbort(reason => {
        if (registered) finish(false, reason);
        else {
          abortRequested = true;
          abortReason = reason;
        }
      });
      if (typeof releaseRegistration !== "function") {
        throw new TypeError("managed async export observation registration is invalid");
      }
      unregister = releaseRegistration;
      registered = true;
      if (abortRequested) finish(false, abortReason);
      else poll();
    } catch (cause) {
      finish(false, new NetWasmManagedError(name, { cause }));
    }
  });
}
