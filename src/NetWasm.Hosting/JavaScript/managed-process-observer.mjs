import {
  appendCleanupFailures,
  executionFailure,
  failedExecutionResult,
  normalExecutionResult,
} from "./execution-result.mjs";

const maxUnsignedInt32 = 0xffffffff;
const minSignedInt32 = -0x80000000;
const maxSignedInt32 = 0x7fffffff;

/**
 * Observe one NetWasm managed-process export without suspending or polling a
 * physical Wasm stack.
 */
export function observeManagedProcess(request = {}) {
  if (request === null || typeof request !== "object") {
    throw new TypeError("managed process observation request is required");
  }

  const { process, subscribeWake, signal = null } = request;
  for (const operation of ["start", "status", "exitCode", "complete"]) {
    if (typeof process?.[operation] !== "function") {
      throw new TypeError(`managed process ${operation} export is required`);
    }
  }
  if (typeof subscribeWake !== "function") {
    throw new TypeError("managed process wake subscription is required");
  }
  if (signal !== null && (typeof signal.aborted !== "boolean"
      || typeof signal.addEventListener !== "function"
      || typeof signal.removeEventListener !== "function")) {
    throw new TypeError("managed process signal must be an AbortSignal");
  }
  if (signal?.aborted) {
    return Promise.resolve(failedExecutionResult(
      "callerCancellation",
      "observation",
      "caller.cancelled",
      "Managed process observation was cancelled by the caller."));
  }

  return new Promise(resolve => {
    let handle;
    let unsubscribe;
    let abortSubscribed = false;
    let settled = false;
    let observing = false;
    let observationPending = false;
    let pendingFailure;

    const finish = outcome => {
      if (settled) return;
      settled = true;
      const cleanupFailures = [];

      try {
        unsubscribe?.();
      } catch {
        cleanupFailures.push(executionFailure(
          "cleanup",
          "host.process-unsubscribe",
          "The execution host could not remove the managed process wake subscription."));
      }

      if (abortSubscribed) {
        try {
          signal.removeEventListener("abort", abort);
        } catch {
          cleanupFailures.push(executionFailure(
            "cleanup",
            "host.process-abort-listener",
            "The execution host could not remove the managed process abort listener."));
        }
      }

      if (handle !== undefined) {
        try {
          process.complete(handle);
        } catch {
          cleanupFailures.push(executionFailure(
            "cleanup",
            "host.process-complete",
            "The execution host could not release the managed process handle."));
        }
      }

      resolve(appendCleanupFailures(outcome, cleanupFailures));
    };

    const abort = () => finish(failedExecutionResult(
      "callerCancellation",
      "observation",
      "caller.cancelled",
      "Managed process observation was cancelled by the caller."));

    const requestObservation = error => {
      if (settled) return;
      if (error !== undefined && pendingFailure === undefined) {
        pendingFailure = failedExecutionResult(
          "hostFailure",
          "observation",
          "host.process-wake",
          "The execution host failed while delivering a managed process wake notification.");
      }
      observationPending = true;
      drainObservations();
    };

    const drainObservations = () => {
      if (settled || handle === undefined || observing) return;
      observing = true;
      try {
        while (observationPending && !settled) {
          observationPending = false;
          if (pendingFailure !== undefined) {
            const outcome = pendingFailure;
            pendingFailure = undefined;
            finish(outcome);
            continue;
          }

          let status;
          try {
            status = process.status(handle);
          } catch {
            finish(failedExecutionResult(
              "hostFailure",
              "observation",
              "host.process-status",
              "The execution host could not read managed process status."));
            continue;
          }
          if (settled || pendingFailure !== undefined) continue;

          switch (status) {
            case 0:
              continue;
            case 1: {
              let exitCode;
              try {
                exitCode = process.exitCode(handle);
              } catch {
                finish(failedExecutionResult(
                  "hostFailure",
                  "observation",
                  "host.process-exit-code",
                  "The execution host could not read the managed process exit code."));
                continue;
              }
              if (settled || pendingFailure !== undefined) continue;
              if (!Number.isInteger(exitCode)
                  || exitCode < minSignedInt32
                  || exitCode > maxSignedInt32) {
                finish(failedExecutionResult(
                  "contractFailure",
                  "observation",
                  "contract.process-exit-code",
                  "The managed process exit code is not a signed 32-bit integer."));
                continue;
              }
              finish(normalExecutionResult(exitCode));
              continue;
            }
            case 2:
              finish(failedExecutionResult(
                "managedFailure",
                "execution",
                "managed.failure",
                "The managed process failed."));
              continue;
            case 3:
              finish(failedExecutionResult(
                "managedCancellation",
                "execution",
                "managed.cancelled",
                "The managed process was cancelled."));
              continue;
            default:
              finish(failedExecutionResult(
                "contractFailure",
                "observation",
                "contract.process-status",
                "The managed process returned an unsupported status."));
          }
        }
      } finally {
        observing = false;
      }
    };

    try {
      const release = subscribeWake(requestObservation);
      if (typeof release !== "function") {
        finish(failedExecutionResult(
          "contractFailure",
          "observation",
          "contract.process-subscription",
          "The managed process wake subscription did not return a release action."));
        return;
      }
      unsubscribe = release;
    } catch {
      finish(failedExecutionResult(
        "hostFailure",
        "observation",
        "host.process-subscription",
        "The execution host could not subscribe to managed process wake notifications."));
      return;
    }

    if (pendingFailure !== undefined) {
      finish(pendingFailure);
      return;
    }

    let started;
    try {
      started = process.start();
    } catch {
      finish(failedExecutionResult(
        "hostFailure",
        "execution",
        "host.process-start",
        "The execution host could not start the managed process."));
      return;
    }
    if (!Number.isInteger(started) || started <= 0 || started > maxUnsignedInt32) {
      finish(failedExecutionResult(
        "contractFailure",
        "execution",
        "contract.process-handle",
        "The managed process handle is not a nonzero unsigned 32-bit integer."));
      return;
    }
    handle = started;

    if (signal !== null) {
      try {
        abortSubscribed = true;
        signal.addEventListener("abort", abort, { once: true });
      } catch {
        finish(failedExecutionResult(
          "hostFailure",
          "observation",
          "host.process-abort-listener",
          "The execution host could not register the managed process abort listener."));
        return;
      }
      if (signal.aborted) {
        abort();
        return;
      }
    }

    requestObservation();
  });
}
