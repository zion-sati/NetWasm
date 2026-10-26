import { isFailureDetail } from "./execution-result.mjs";
import { consoleLine, createLineOutput, readEnvironmentOverride } from "./session-options.mjs";
import { readManagedExceptionDetails } from "./managed-exception-details.mjs";

const optionKeys = ["generation", "operations", "startup", "worker"];
const optionalOptionKeys = ["environment", "onNotification", "stderr", "stdout"];
const outputStreams = new Set(["stderr", "stdout"]);
const protocolVersion = 2;

export class NetWasmWorkerError extends Error {
  constructor(message, {
    category = "hostFailure", detail = null, operation = null, remoteStack = null, managed = null,
  } = {}) {
    super(message);
    this.name = "NetWasmWorkerError";
    this.category = category;
    this.detail = detail;
    this.operation = operation;
    this.remoteStack = remoteStack;
    this.managed = managed === null ? null : readManagedExceptionDetails(managed);
  }
}

export function createWorkerSessionClient(options) {
  assertExactDataObject(options, [
    ...optionKeys,
    ...optionalOptionKeys.filter(key => Object.hasOwn(options ?? {}, key)),
  ].sort(), "worker session client options");
  let onNotification = options.onNotification ?? null;
  if (onNotification !== null && typeof onNotification !== "function") {
    throw new TypeError("worker notification handler must be a function");
  }
  for (const name of ["stderr", "stdout"]) {
    if (options[name] !== undefined && options[name] !== null && typeof options[name] !== "function") {
      throw new TypeError(`worker ${name} must be a function`);
    }
  }
  const environment = readEnvironmentOverride(options.environment ?? undefined, "worker environment")
    ?? Object.freeze({});
  const output = Object.freeze({
    stderr: createLineOutput(options.stderr ?? consoleLine("error")),
    stdout: createLineOutput(options.stdout ?? consoleLine("log")),
  });
  let outputFlushed = false;
  validateGeneration(options.generation);
  const operations = snapshotOperations(options.operations);
  const startup = snapshotStartup(options.startup);
  const worker = readWorker(options.worker);
  let state = "starting";
  let nextRequestId = 1;
  // Calls run concurrently in the worker, as page calls do. Calls made before
  // the worker is ready wait in order.
  const inFlight = new Map();
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
    worker.postMessage({ ...baseMessage("initialize"), environment, startup });
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
      let signal;
      try {
        signal = snapshotCallOptions(callOptions);
      } catch (cause) {
        return Promise.reject(cause);
      }
      if (disposeRequested || state === "terminated" || state === "failed") {
        return Promise.reject(new NetWasmWorkerError("worker session is closed"));
      }
      if (signal?.aborted) return Promise.reject(signal.reason);
      const requestId = nextRequestId++;
      const completion = new Promise((resolve, reject) => {
        const request = {
          requestId,
          operation,
          arguments: snapshotArguments(arguments_),
          resolve,
          reject,
          settled: false,
          signal,
          abort: null,
        };
        request.abort = () => abortRequest(request);
        signal?.addEventListener("abort", request.abort, { once: true });
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
    if (response.kind === "output") {
      output[response.stream].sink.write(response.bytes);
      return;
    }
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
      if (!disposeRequested || inFlight.size !== 0 || queued.length !== 0 || !closeSent) {
        throw new TypeError("worker closed unexpectedly");
      }
      terminate(null, true);
      return;
    }
    if (response.kind === "failure" && response.requestId === null) {
      throw remoteError(response.error);
    }
    const request = inFlight.get(response.requestId);
    if (request === undefined) {
      throw new TypeError("worker response request identity is invalid");
    }
    inFlight.delete(response.requestId);
    if (!request.settled) {
      request.settled = true;
      cleanupRequest(request);
      if (response.kind === "result") request.resolve(response.value);
      else request.reject(cancellationOrRemoteError(response.error));
    }
    dispatch();
  }

  // The caller stops waiting: a call the worker has not received is never
  // sent, and a running one is abandoned, its eventual reply dropped. Asking
  // the export itself to stop is an ordinary call to another export.
  function abortRequest(request) {
    if (request.settled) return;
    const index = queued.indexOf(request);
    if (index >= 0) queued.splice(index, 1);
    request.settled = true;
    cleanupRequest(request);
    request.reject(request.signal.reason);
    dispatch();
  }

  function dispatch() {
    if (state !== "ready") return;
    while (queued.length !== 0) {
      const request = queued.shift();
      inFlight.set(request.requestId, request);
      try {
        worker.postMessage({
          ...baseMessage("invoke"),
          requestId: request.requestId,
          operation: request.operation,
          arguments: request.arguments,
        });
      } catch (cause) {
        fail(cause);
        return;
      }
    }
    // Disposal lets every accepted call finish, abandoned ones included.
    if (disposeRequested && !closeSent && inFlight.size === 0) {
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
    flushOutput();
    if (wasStarting) rejectReady(error);
    for (const request of [...inFlight.values(), ...queued.splice(0)]) rejectRequest(request, error);
    inFlight.clear();
    rejectDispose?.(error);
    removeListeners();
    try { worker.terminate(); } catch {}
  }

  function terminate(cause, graceful) {
    const wasStarting = state === "starting";
    state = "terminated";
    flushOutput();
    removeListeners();
    try { worker.terminate(); } catch {}
    if (cause !== null) {
      if (wasStarting) rejectReady(cause);
      for (const request of [...inFlight.values(), ...queued.splice(0)]) rejectRequest(request, cause);
      rejectDispose?.(cause);
    } else if (graceful) {
      resolveDispose?.();
    }
    inFlight.clear();
  }

  // Delivers each stream's final partial line once the guest can write no more.
  function flushOutput() {
    if (outputFlushed) return;
    outputFlushed = true;
    output.stdout.flush();
    output.stderr.flush();
  }

  function removeListeners() {
    onNotification = null;
    worker.removeEventListener("message", onMessage);
    worker.removeEventListener("error", onError);
    worker.removeEventListener("messageerror", onMessageError);
  }

  function rejectRequest(request, error) {
    if (request.settled) return;
    request.settled = true;
    cleanupRequest(request);
    request.reject(error);
  }

  function baseMessage(kind) {
    return { protocolVersion, generation: options.generation, kind };
  }
}

function snapshotCallOptions(value) {
  if (value === undefined) return null;
  if (value === null || typeof value !== "object" || Array.isArray(value)
      || Object.getOwnPropertySymbols(value).length !== 0) {
    throw new TypeError("worker call options are invalid");
  }
  const descriptors = Object.getOwnPropertyDescriptors(value);
  if (Object.keys(descriptors).some(key => key !== "signal")
      || Object.values(descriptors).some(
        descriptor => !descriptor.enumerable || !("value" in descriptor))) {
    throw new TypeError("worker call options shape is invalid");
  }
  const signal = value.signal ?? null;
  if (signal !== null && !(signal instanceof AbortSignal)) {
    throw new TypeError("worker call signal must be an AbortSignal");
  }
  return signal;
}

function snapshotArguments(values) {
  return values.map(value => value instanceof Uint8Array ? new Uint8Array(value) : value);
}

function cleanupRequest(request) {
  request.signal?.removeEventListener("abort", request.abort);
}

// A task the export canceled rejects with an AbortError, as on the page.
function cancellationOrRemoteError(value) {
  return value.category === "cancelled" ? new DOMException(value.message, "AbortError") : remoteError(value);
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
  if (value.kind === "output") {
    assertKeys(descriptors, ["bytes", "generation", "kind", "protocolVersion", "stream"], "worker output");
    if (!outputStreams.has(value.stream) || !(value.bytes instanceof Uint8Array)) {
      throw new TypeError("worker output is invalid");
    }
    return value;
  }
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
    ? ["category", "detail", "managed", "message", "operation", "stack"]
    : ["category", "detail", "message", "operation", "stack"], "worker failure envelope");
  if (Object.values(descriptors).some(descriptor => !descriptor.enumerable || !("value" in descriptor))) {
    throw new TypeError("worker failure envelope must contain data properties");
  }
  const managed = Object.hasOwn(descriptors, "managed") ? readManagedExceptionDetails(value.managed) : null;
  if (typeof value.category !== "string" || typeof value.message !== "string"
      || value.message.length === 0 && managed?.message !== ""
      || managed !== null && value.category !== "guestFailure"
      || value.detail !== null && !isFailureDetail(value.detail)
      || value.operation !== null && typeof value.operation !== "string"
      || value.stack !== null && typeof value.stack !== "string") {
    throw new TypeError("worker failure envelope is invalid");
  }
}

function remoteError(value) {
  return new NetWasmWorkerError(value.message, {
    category: value.category,
    detail: value.detail,
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
