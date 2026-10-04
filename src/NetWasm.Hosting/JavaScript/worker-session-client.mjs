import { readManagedExceptionDetails } from "./managed-exception-details.mjs";

const optionKeys = ["generation", "operations", "startup", "worker"];
const protocolVersion = 1;

export class NetWasmWorkerError extends Error {
  constructor(message, { category = "hostFailure", operation = null, remoteStack = null, managed = null } = {}) {
    super(message);
    this.name = "NetWasmWorkerError";
    this.category = category;
    this.operation = operation;
    this.remoteStack = remoteStack;
    this.managed = managed === null ? null : readManagedExceptionDetails(managed);
  }
}

export function createWorkerSessionClient(options) {
  assertExactDataObject(options, Object.hasOwn(options ?? {}, "onNotification")
    ? [...optionKeys, "onNotification"].sort() : optionKeys, "worker session client options");
  let onNotification = options.onNotification ?? null;
  if (onNotification !== null && typeof onNotification !== "function") {
    throw new TypeError("worker notification handler must be a function");
  }
  validateGeneration(options.generation);
  const operations = snapshotOperations(options.operations);
  const startup = snapshotStartup(options.startup);
  const worker = readWorker(options.worker);
  let state = "starting";
  let nextRequestId = 1;
  let active = null;
  const queued = [];
  let disposeRequested = false;
  let closeSent = false;
  let terminalFailure = null;
  let resolveReady;
  let rejectReady;
  let resolveDispose;
  let rejectDispose;
  let disposeCompletion;
  const ready = new Promise((resolve, reject) => {
    resolveReady = resolve;
    rejectReady = reject;
  });

  const onMessage = event => {
    try { receive(event?.data); } catch (cause) { fail(cause); }
  };
  const onError = event => fail(new NetWasmWorkerError(
    typeof event?.message === "string" && event.message.length !== 0
      ? event.message
      : "worker execution failed"));
  const onMessageError = () => fail(new NetWasmWorkerError("worker message delivery failed"));
  worker.addEventListener("message", onMessage);
  worker.addEventListener("error", onError);
  worker.addEventListener("messageerror", onMessageError);

  try {
    worker.postMessage({ ...baseMessage("initialize"), startup });
  } catch (cause) {
    fail(cause);
  }

  return Object.freeze({
    generation: options.generation,
    ready,
    invoke(operation, arguments_ = [], callOptions = undefined) {
      if (!operations.has(operation)) {
        return Promise.reject(new TypeError("worker operation is unavailable"));
      }
      if (!Array.isArray(arguments_)) {
        return Promise.reject(new TypeError("worker arguments must be an array"));
      }
      let cancellation;
      try {
        cancellation = snapshotCallOptions(callOptions, operation);
      } catch (cause) {
        return Promise.reject(cause);
      }
      if (disposeRequested || state === "terminated" || state === "failed") {
        return Promise.reject(new NetWasmWorkerError("worker session is closed"));
      }
      if (cancellation.signal?.aborted) {
        return Promise.reject(callError("cancelled", operation));
      }
      const requestId = nextRequestId++;
      const completion = new Promise((resolve, reject) => {
        const request = {
          requestId,
          operation,
          arguments: snapshotArguments(arguments_),
          resolve,
          reject,
          settled: false,
          signal: cancellation.signal,
          abort: null,
          timer: null,
        };
        request.abort = () => cancelRequest(request, "cancelled");
        request.signal?.addEventListener("abort", request.abort, { once: true });
        if (cancellation.timeoutMilliseconds !== null) {
          request.timer = setTimeout(
            () => cancelRequest(request, "deadlineExceeded"),
            cancellation.timeoutMilliseconds);
        }
        queued.push(request);
      });
      dispatch();
      return completion;
    },
    dispose() {
      if (disposeCompletion !== undefined) return disposeCompletion;
      if (state === "terminated") return Promise.resolve();
      if (state === "failed") {
        disposeCompletion = Promise.reject(terminalFailure);
        return disposeCompletion;
      }
      disposeRequested = true;
      disposeCompletion = new Promise((resolve, reject) => {
        resolveDispose = resolve;
        rejectDispose = reject;
      });
      dispatch();
      return disposeCompletion;
    },
    terminate() {
      if (state === "terminated") return;
      terminate(new NetWasmWorkerError("worker session was terminated"), false);
    },
  });

  function receive(message) {
    if (state === "terminated") return;
    const response = validateResponse(message, options.generation);
    if (response === null) return;
    if (response.kind === "notification") {
      if (onNotification !== null) {
        try {
          Promise.resolve(onNotification(Object.freeze({
            operation: response.operation, arguments: response.arguments,
          }))).catch(reportNotificationError);
        } catch (cause) { reportNotificationError(cause); }
      }
      return;
    }
    if (response.kind === "ready") {
      if (state !== "starting" || !sameOperations(response.operations, operations)) {
        throw new TypeError("worker readiness contract is invalid");
      }
      state = "ready";
      resolveReady();
      dispatch();
      return;
    }
    if (response.kind === "closed") {
      if (!disposeRequested || active !== null || queued.length !== 0 || !closeSent) {
        throw new TypeError("worker closed unexpectedly");
      }
      terminate(null, true);
      return;
    }
    if (response.kind === "failure" && response.requestId === null) {
      throw remoteError(response.error);
    }
    if (active === null || response.requestId !== active.requestId) {
      throw new TypeError("worker response request identity is invalid");
    }
    const request = active;
    active = null;
    if (!request.settled) {
      request.settled = true;
      cleanupRequest(request);
      if (response.kind === "result") request.resolve(response.value);
      else request.reject(remoteError(response.error));
    }
    dispatch();
  }

  function cancelRequest(request, category) {
    if (request.settled) return;
    request.settled = true;
    cleanupRequest(request);
    if (request !== active) {
      const index = queued.indexOf(request);
      if (index >= 0) queued.splice(index, 1);
    }
    request.reject(callError(category, request.operation));
    dispatch();
  }

  function dispatch() {
    if (state === "starting" || state === "failed" || state === "terminated" || active !== null) {
      return;
    }
    if (queued.length !== 0) {
      active = queued.shift();
      try {
        worker.postMessage({
          ...baseMessage("invoke"),
          requestId: active.requestId,
          operation: active.operation,
          arguments: active.arguments,
        });
      } catch (cause) {
        fail(cause);
      }
      return;
    }
    if (disposeRequested && !closeSent) {
      closeSent = true;
      try { worker.postMessage(baseMessage("close")); } catch (cause) { fail(cause); }
    }
  }

  function fail(cause) {
    if (state === "failed" || state === "terminated") return;
    terminalFailure = cause instanceof Error
      ? cause
      : new NetWasmWorkerError("worker session failed");
    const error = terminalFailure;
    const wasStarting = state === "starting";
    state = "failed";
    if (wasStarting) rejectReady(error);
    rejectRequest(active, error);
    active = null;
    for (const request of queued.splice(0)) rejectRequest(request, error);
    rejectDispose?.(error);
    removeListeners();
    try { worker.terminate(); } catch {}
  }

  function terminate(cause, graceful) {
    const wasStarting = state === "starting";
    state = "terminated";
    removeListeners();
    try { worker.terminate(); } catch {}
    if (cause !== null) {
      if (wasStarting) rejectReady(cause);
      rejectRequest(active, cause);
      for (const request of queued.splice(0)) rejectRequest(request, cause);
      rejectDispose?.(cause);
    } else if (graceful) {
      resolveDispose?.();
    }
    active = null;
  }

  function removeListeners() {
    onNotification = null;
    worker.removeEventListener("message", onMessage);
    worker.removeEventListener("error", onError);
    worker.removeEventListener("messageerror", onMessageError);
  }

  function rejectRequest(request, error) {
    if (request === null || request.settled) return;
    request.settled = true;
    cleanupRequest(request);
    request.reject(error);
  }

  function baseMessage(kind) {
    return { protocolVersion, generation: options.generation, kind };
  }
}

function snapshotCallOptions(value, operation) {
  if (value === undefined) return Object.freeze({ signal: null, timeoutMilliseconds: null });
  if (value === null || typeof value !== "object" || Array.isArray(value)
      || Object.getOwnPropertySymbols(value).length !== 0) {
    throw new TypeError("worker call options are invalid");
  }
  const descriptors = Object.getOwnPropertyDescriptors(value);
  const keys = Object.keys(descriptors);
  if (keys.some(key => key !== "signal" && key !== "timeoutMilliseconds")
      || Object.values(descriptors).some(
        descriptor => !descriptor.enumerable || !("value" in descriptor))) {
    throw new TypeError("worker call options shape is invalid");
  }
  const signal = value.signal ?? null;
  const timeoutMilliseconds = value.timeoutMilliseconds ?? null;
  if (signal !== null && !(signal instanceof AbortSignal)) {
    throw new TypeError("worker call signal must be an AbortSignal");
  }
  if (timeoutMilliseconds !== null
      && (!Number.isSafeInteger(timeoutMilliseconds) || timeoutMilliseconds < 0
        || timeoutMilliseconds > 2_147_483_647)) {
    throw new TypeError("worker call timeout must be a non-negative integer");
  }
  return Object.freeze({ signal, timeoutMilliseconds, operation });
}

function snapshotArguments(values) {
  return values.map(value => value instanceof Uint8Array ? new Uint8Array(value) : value);
}

function cleanupRequest(request) {
  request.signal?.removeEventListener("abort", request.abort);
  if (request.timer !== null) clearTimeout(request.timer);
  request.timer = null;
}

function callError(category, operation) {
  const message = category === "cancelled"
    ? "worker call was cancelled"
    : "worker call deadline was exceeded";
  return new NetWasmWorkerError(message, { category, operation });
}

function readWorker(value) {
  if (value === null || typeof value !== "object" || [
    "addEventListener", "removeEventListener", "postMessage", "terminate",
  ].some(name => typeof value[name] !== "function")) {
    throw new TypeError("worker session client requires a Worker");
  }
  return value;
}

function snapshotOperations(value) {
  if (!Array.isArray(value) || value.some(operation => typeof operation !== "string"
      || operation.length === 0) || new Set(value).size !== value.length) {
    throw new TypeError("worker operation allowlist is invalid");
  }
  return new Set(value);
}

function snapshotStartup(value) {
  assertExactDataObject(value, ["buildFingerprint", "manifestSha256"], "worker startup");
  validateDigest(value.buildFingerprint, "worker build fingerprint");
  validateDigest(value.manifestSha256, "worker manifest digest");
  return Object.freeze({
    buildFingerprint: value.buildFingerprint,
    manifestSha256: value.manifestSha256,
  });
}

function validateResponse(value, generation) {
  if (value === null || typeof value !== "object" || Array.isArray(value)
      || Object.getOwnPropertySymbols(value).length !== 0) {
    throw new TypeError("worker response is invalid");
  }
  const descriptors = Object.getOwnPropertyDescriptors(value);
  if (Object.values(descriptors).some(descriptor => !descriptor.enumerable || !("value" in descriptor))) {
    throw new TypeError("worker response must contain data properties");
  }
  if (value.protocolVersion !== protocolVersion) {
    throw new TypeError("worker response protocol is invalid");
  }
  if (value.generation !== generation) return null;
  if (value.kind === "notification") {
    assertKeys(descriptors,
      ["arguments", "generation", "kind", "operation", "protocolVersion"], "worker notification");
    if (typeof value.operation !== "string" || value.operation.length === 0
        || !Array.isArray(value.arguments)) throw new TypeError("worker notification is invalid");
    return value;
  }
  if (value.kind === "ready") {
    assertKeys(descriptors, ["generation", "kind", "operations", "protocolVersion"], "worker readiness");
    if (!Array.isArray(value.operations)) throw new TypeError("worker operations are invalid");
    return value;
  }
  if (value.kind === "closed") {
    assertKeys(descriptors, ["generation", "kind", "protocolVersion"], "worker close response");
    return value;
  }
  if (value.kind === "result") {
    assertKeys(descriptors,
      ["generation", "kind", "protocolVersion", "requestId", "value"],
      "worker result");
  } else if (value.kind === "failure") {
    assertKeys(descriptors,
      ["error", "generation", "kind", "protocolVersion", "requestId"],
      "worker failure");
    validateError(value.error);
  } else {
    throw new TypeError("worker response kind is invalid");
  }
  if (value.requestId !== null
      && (!Number.isSafeInteger(value.requestId) || value.requestId <= 0)) {
    throw new TypeError("worker response request identity is invalid");
  }
  return value;
}

function validateError(value) {
  if (value === null || typeof value !== "object" || Array.isArray(value)
      || Object.getOwnPropertySymbols(value).length !== 0) {
    throw new TypeError("worker failure envelope is invalid");
  }
  const descriptors = Object.getOwnPropertyDescriptors(value);
  assertKeys(descriptors, Object.hasOwn(descriptors, "managed")
    ? ["category", "managed", "message", "operation", "stack"]
    : ["category", "message", "operation", "stack"], "worker failure envelope");
  if (Object.values(descriptors).some(descriptor => !descriptor.enumerable || !("value" in descriptor))) {
    throw new TypeError("worker failure envelope must contain data properties");
  }
  const managed = Object.hasOwn(descriptors, "managed") ? readManagedExceptionDetails(value.managed) : null;
  if (typeof value.category !== "string" || typeof value.message !== "string"
      || value.message.length === 0 && managed?.message !== ""
      || managed !== null && value.category !== "guestFailure"
      || value.operation !== null && typeof value.operation !== "string"
      || value.stack !== null && typeof value.stack !== "string") {
    throw new TypeError("worker failure envelope is invalid");
  }
}

function remoteError(value) {
  return new NetWasmWorkerError(value.message, {
    category: value.category,
    operation: value.operation,
    remoteStack: value.stack,
    managed: value.managed ?? null,
  });
}

function sameOperations(value, operations) {
  return Array.isArray(value) && value.length === operations.size
    && new Set(value).size === value.length
    && value.every(operation => operations.has(operation));
}

function validateGeneration(value) {
  if (typeof value !== "string" || value.length === 0 || value.length > 128
      || /[\u0000-\u001f\u007f]/u.test(value)) {
    throw new TypeError("worker generation is invalid");
  }
}

function validateDigest(value, label) {
  if (typeof value !== "string" || !/^[0-9a-f]{64}$/u.test(value)) {
    throw new TypeError(`${label} is invalid`);
  }
}

function assertKeys(descriptors, keys, label) {
  const actual = Object.keys(descriptors).sort();
  if (actual.length !== keys.length || actual.some((key, index) => key !== keys[index])) {
    throw new TypeError(`${label} shape is invalid`);
  }
}

function assertExactDataObject(value, keys, label) {
  if (value === null || typeof value !== "object" || Array.isArray(value)
      || Object.getOwnPropertySymbols(value).length !== 0) {
    throw new TypeError(`${label} is invalid`);
  }
  const descriptors = Object.getOwnPropertyDescriptors(value);
  const actual = Object.keys(descriptors).sort();
  if (actual.length !== keys.length || actual.some((key, index) => key !== keys[index])
      || Object.values(descriptors).some(descriptor => !descriptor.enumerable || !("value" in descriptor))) {
    throw new TypeError(`${label} shape is invalid`);
  }
}

function reportNotificationError(cause) {
  if (typeof globalThis.reportError === "function") globalThis.reportError(cause);
  else console.error(cause);
}
