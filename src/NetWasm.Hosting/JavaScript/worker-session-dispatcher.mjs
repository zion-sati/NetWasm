import { describeFailureCause } from "./execution-result.mjs";
import { NetWasmManagedError } from "./managed-errors.mjs";

const optionKeys = ["close", "exports", "generation", "onFailure", "post"];
const protocolVersion = 2;

export function createWorkerSessionDispatcher(options) {
  assertExactDataObject(options, optionKeys, "worker session dispatcher options");
  if (typeof options.close !== "function" || typeof options.onFailure !== "function"
      || typeof options.post !== "function") {
    throw new TypeError("worker session dispatcher actions are required");
  }
  validateGeneration(options.generation);
  const operations = snapshotOperations(options.exports);
  // Calls run concurrently, as page calls do: each starts when it arrives, and
  // an asynchronous export's awaits let the next one run.
  const active = new Set();
  let closeRequested = false;
  let closeCompletion;
  let closed = false;

  const failTerminal = cause => {
    try { options.onFailure(cause); } catch {}
  };
  const tryPost = (message, transfer = []) => {
    try {
      options.post(Object.freeze(message), transfer);
      return null;
    } catch (cause) {
      return { cause };
    }
  };
  const postOrFail = message => {
    const deliveryFailure = tryPost(message);
    if (deliveryFailure !== null) failTerminal(deliveryFailure.cause);
  };
  const close = () => {
    if (closeCompletion !== undefined) return closeCompletion;
    closeRequested = true;
    if (active.size !== 0) return undefined;
    closeCompletion = Promise.resolve().then(options.close).then(
      () => {
        closed = true;
        postOrFail(baseMessage("closed"));
      },
      cause => {
        closed = true;
        const error = failureEnvelope(cause, "close", "hostFailure");
        tryPost({ ...baseMessage("failure"), requestId: null, error });
        failTerminal(cause);
      });
    return closeCompletion;
  };

  return Object.freeze({
    get operations() { return Object.freeze([...operations.keys()]); },
    async receive(message) {
      const request = validateMessage(message, options.generation);
      if (request.kind === "close") {
        close();
        return;
      }
      if (active.has(request.requestId)) {
        throw new TypeError("worker request identity is already active");
      }
      if (closed || closeRequested) {
        postOrFail(failedResponse(
          request, new Error("worker session is closing"), "hostFailure"));
        return;
      }
      const operation = operations.get(request.operation);
      if (operation === undefined) {
        postOrFail(failedResponse(
          request, new Error("worker operation is unavailable"), "contractFailure"));
        return;
      }
      active.add(request.requestId);
      try {
        const value = await operation(...request.arguments);
        const deliveryFailure = tryPost({
          ...baseMessage("result"), requestId: request.requestId, value,
        }, transferable(value));
        if (deliveryFailure !== null) {
          postOrFail(failedResponse(request, deliveryFailure.cause, "hostFailure"));
        }
      } catch (cause) {
        postOrFail(failedResponse(request, cause, category(cause)));
      } finally {
        active.delete(request.requestId);
        if (closeRequested) close();
      }
    },
  });

  function baseMessage(kind) {
    return { protocolVersion, generation: options.generation, kind };
  }
}

function snapshotOperations(value) {
  if (value === null || typeof value !== "object" || Array.isArray(value)
      || Object.getOwnPropertySymbols(value).length !== 0
      || Object.getPrototypeOf(value) !== null && Object.getPrototypeOf(value) !== Object.prototype) {
    throw new TypeError("worker session exports are invalid");
  }
  const operations = new Map();
  for (const [name, descriptor] of Object.entries(Object.getOwnPropertyDescriptors(value))) {
    if (!descriptor.enumerable || !("value" in descriptor) || typeof descriptor.value !== "function"
        || name.length === 0 || name === "__proto__" || name === "prototype"
        || name === "constructor") {
      throw new TypeError("worker session export is invalid");
    }
    operations.set(name, descriptor.value);
  }
  return operations;
}

function validateMessage(value, generation) {
  if (value === null || typeof value !== "object" || Array.isArray(value)
      || Object.getOwnPropertySymbols(value).length !== 0) {
    throw new TypeError("worker request is invalid");
  }
  const descriptors = Object.getOwnPropertyDescriptors(value);
  if (Object.values(descriptors).some(descriptor => !descriptor.enumerable || !("value" in descriptor))) {
    throw new TypeError("worker request must contain data properties");
  }
  if (value.protocolVersion !== protocolVersion || value.generation !== generation) {
    throw new TypeError("worker request identity is invalid");
  }
  if (value.kind === "close") {
    assertKeys(descriptors, ["generation", "kind", "protocolVersion"], "worker close request");
    return value;
  }
  assertKeys(descriptors,
    ["arguments", "generation", "kind", "operation", "protocolVersion", "requestId"],
    "worker invocation request");
  if (value.kind !== "invoke" || !Number.isSafeInteger(value.requestId) || value.requestId <= 0
      || typeof value.operation !== "string" || value.operation.length === 0
      || !Array.isArray(value.arguments)) {
    throw new TypeError("worker invocation request is invalid");
  }
  return value;
}

// An array a call returns is the worker's own copy, so it moves to the page
// instead of being copied again.
function transferable(value) {
  return ArrayBuffer.isView(value) && value.buffer instanceof ArrayBuffer
    && value.byteOffset === 0 && value.byteLength === value.buffer.byteLength
    ? [value.buffer]
    : [];
}

function failedResponse(request, cause, failureCategory) {
  return {
    protocolVersion,
    generation: request.generation,
    kind: "failure",
    requestId: request.requestId,
    error: failureEnvelope(cause, request.operation, failureCategory),
  };
}

function failureEnvelope(cause, operation, failureCategory) {
  const error = cause instanceof Error ? cause : new Error("worker invocation failed");
  const managed = error instanceof NetWasmManagedError ? error.managed : null;
  return Object.freeze({
    category: failureCategory,
    detail: describeFailureCause(cause) ?? null,
    message: managed !== null ? error.message : error.message || "worker invocation failed",
    operation,
    stack: typeof error.stack === "string" ? error.stack : null,
    ...(managed === null ? {} : { managed }),
  });
}

function category(cause) {
  // A canceled task rejects with an AbortError, on the page as here.
  if (cause?.name === "AbortError") return "cancelled";
  if (cause instanceof NetWasmManagedError) return "guestFailure";
  if (cause instanceof WebAssembly.RuntimeError) return "trap";
  return "hostFailure";
}

function validateGeneration(value) {
  if (typeof value !== "string" || value.length === 0 || value.length > 128
      || /[\u0000-\u001f\u007f]/u.test(value)) {
    throw new TypeError("worker generation is invalid");
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
