import { createWorkerSessionDispatcher } from "./worker-session-dispatcher.mjs";

const optionKeys = ["openSession", "post", "terminate"];
const protocolVersion = 1;

export function createWorkerSessionHost(options) {
  assertExactDataObject(options, optionKeys, "worker session host options");
  if (typeof options.openSession !== "function" || typeof options.post !== "function"
      || typeof options.terminate !== "function") {
    throw new TypeError("worker session host actions are required");
  }
  let state = "waiting";
  let generation = null;
  let dispatcher = null;

  return Object.freeze({
    async receive(message) {
      if (state === "terminated") return;
      if (state === "waiting") {
        const initialize = validateInitialize(message);
        generation = initialize.generation;
        state = "starting";
        try {
          const session = readSession(await options.openSession(Object.freeze({
            generation,
            notify,
            startup: initialize.startup,
          })));
          if (state === "terminated") {
            await session.close();
            return;
          }
          dispatcher = createWorkerSessionDispatcher({
            close: session.close,
            exports: session.exports,
            generation,
            onFailure: cause => {
              terminate(postTerminalFailure(cause, "delivery"));
            },
            post: message => {
              options.post(message);
              if (message.kind === "closed") state = "terminated";
            },
          });
          state = "ready";
          options.post(Object.freeze({
            protocolVersion,
            generation,
            kind: "ready",
            operations: dispatcher.operations,
          }));
          session.failure.then(cause => {
            if (cause !== null && state !== "terminated") {
              terminate(postTerminalFailure(cause, "session"));
            }
          }, cause => {
            if (state !== "terminated") {
              terminate(postTerminalFailure(cause, "session"));
            }
          });
        } catch (cause) {
          terminate(postTerminalFailure(cause, "startup"));
        }
        return;
      }
      if (state !== "ready") {
        const cause = new Error("worker is not ready");
        terminate(postTerminalFailure(cause, "startup"));
        return;
      }
      try {
        await dispatcher.receive(message);
      } catch (cause) {
        terminate(postTerminalFailure(cause, "protocol"));
      }
    },
    terminate,
  });

  function notify(operation, arguments_ = []) {
    if (state === "terminated") return;
    if (typeof operation !== "string" || operation.length === 0 || !Array.isArray(arguments_)) {
      throw new TypeError("worker notification requires a name and argument array");
    }
    options.post(Object.freeze({
      protocolVersion, generation, kind: "notification", operation, arguments: arguments_,
    }));
  }

  function terminate(undeliveredCause = null) {
    if (state === "terminated") return;
    state = "terminated";
    try { options.terminate(undeliveredCause); } catch {}
  }

  function postTerminalFailure(cause, operation) {
    const error = cause instanceof Error ? cause : new Error("worker failed");
    try {
      options.post(Object.freeze({
        protocolVersion,
        generation,
        kind: "failure",
        requestId: null,
        error: Object.freeze({
          category: operation === "startup" ? "startupFailure" : "hostFailure",
          message: error.message || "worker failed",
          operation,
          stack: typeof error.stack === "string" ? error.stack : null,
        }),
      }));
      return null;
    } catch {
      return error;
    }
  }
}

function validateInitialize(value) {
  if (value === null || typeof value !== "object" || Array.isArray(value)
      || Object.getOwnPropertySymbols(value).length !== 0) {
    throw new TypeError("worker initialization is invalid");
  }
  const descriptors = Object.getOwnPropertyDescriptors(value);
  const keys = Object.keys(descriptors).sort();
  const expected = ["generation", "kind", "protocolVersion", "startup"];
  if (keys.length !== expected.length || keys.some((key, index) => key !== expected[index])
      || Object.values(descriptors).some(descriptor => !descriptor.enumerable || !("value" in descriptor))
      || value.protocolVersion !== protocolVersion || value.kind !== "initialize") {
    throw new TypeError("worker initialization contract is invalid");
  }
  validateGeneration(value.generation);
  const startup = snapshotStartup(value.startup);
  return Object.freeze({ generation: value.generation, startup });
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

function readSession(value) {
  if (value === null || typeof value !== "object" || Array.isArray(value)
      || value.exports === null || typeof value.exports !== "object"
      || typeof value.close !== "function" || !(value.failure instanceof Promise)) {
    throw new TypeError("worker session opener returned an invalid session");
  }
  return Object.freeze({
    close: value.close.bind(value),
    exports: value.exports,
    failure: value.failure,
  });
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
