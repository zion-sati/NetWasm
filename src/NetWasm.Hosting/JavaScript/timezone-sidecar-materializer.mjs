import { snapshotDeploymentArtifact } from "./deployment-artifact.mjs";

const requestKeys = new Set([
  "hashBytes",
  "mountReadOnlyFile",
  "readArtifact",
  "selection",
  "signal",
]);
const requiredRequestKeys = Object.freeze([
  "hashBytes",
  "mountReadOnlyFile",
  "readArtifact",
  "selection",
]);
const selectionKeys = ["artifact", "timeZone"];
const digestPattern = /^[0-9a-f]{64}$/u;
const guestPath = "/netwasm-timezones/netwasm-timezones.nwtz";

export async function materializeTimeZoneSidecar(request = {}) {
  assertRequest(request);
  const {
    selection,
    readArtifact,
    hashBytes,
    mountReadOnlyFile,
    signal = null,
  } = request;
  assertFunction(readArtifact, "timezone sidecar reader");
  assertFunction(hashBytes, "timezone sidecar hasher");
  assertFunction(mountReadOnlyFile, "timezone read-only mount act");
  validateSignal(signal);
  if (selection === null) return null;

  const selected = snapshotSelection(selection);
  throwIfAborted(signal);
  const transportBytes = await readArtifact(selected.artifact, signal);
  throwIfAborted(signal);
  if (!(transportBytes instanceof Uint8Array)) {
    throw new TypeError("timezone sidecar reader returned invalid bytes");
  }
  const bytes = new Uint8Array(transportBytes);
  const actualDigest = await hashBytes(new Uint8Array(bytes));
  throwIfAborted(signal);
  if (typeof actualDigest !== "string" || !digestPattern.test(actualDigest)) {
    throw new TypeError("timezone sidecar hasher returned an invalid digest");
  }
  if (actualDigest !== selected.artifact.sha256) {
    throw new Error("timezone sidecar integrity check failed");
  }

  const release = mountReadOnlyFile(Object.freeze({
    guestPath,
    bytes: new Uint8Array(bytes),
  }));
  if (typeof release !== "function") {
    throw new TypeError("timezone read-only mount act returned an invalid release action");
  }
  return Object.freeze({
    code: "host.timezone-mount",
    message: "The execution host could not release the timezone sidecar mount.",
    release,
  });
}

function snapshotSelection(value) {
  assertExactDataObject(value, selectionKeys, "timezone sidecar selection");
  if (typeof value.timeZone !== "string" || value.timeZone.length === 0
      || value.timeZone.includes("\0") || value.timeZone === "UTC"
      || value.timeZone === "Etc/UTC") {
    throw new TypeError("timezone sidecar selection requires a non-UTC TZ");
  }
  const artifact = snapshotDeploymentArtifact(value.artifact);
  if (artifact.role !== "timezone-data"
      || artifact.mediaType !== "application/octet-stream"
      || artifact.schemaVersion !== 1
      || !artifact.relativePath.endsWith(".wasm.tz-info")) {
    throw new TypeError("timezone sidecar selection has an invalid artifact contract");
  }
  return Object.freeze({ timeZone: value.timeZone, artifact });
}

function assertRequest(request) {
  if (request === null || typeof request !== "object" || Array.isArray(request)
      || Object.getOwnPropertySymbols(request).length !== 0) {
    throw new TypeError("timezone sidecar materialization request is invalid");
  }
  const prototype = Object.getPrototypeOf(request);
  const descriptors = Object.getOwnPropertyDescriptors(request);
  const keys = Object.keys(descriptors);
  if (prototype !== null && prototype !== Object.prototype
      || keys.some(key => !requestKeys.has(key))
      || requiredRequestKeys.some(key => !Object.hasOwn(descriptors, key))
      || Object.values(descriptors).some(descriptor => !descriptor.enumerable || !("value" in descriptor))) {
    throw new TypeError("timezone sidecar materialization request shape is invalid");
  }
}

function assertExactDataObject(value, keys, label) {
  if (value === null || typeof value !== "object" || Array.isArray(value)
      || Object.getOwnPropertySymbols(value).length !== 0) {
    throw new TypeError(`${label} is invalid`);
  }
  const prototype = Object.getPrototypeOf(value);
  const descriptors = Object.getOwnPropertyDescriptors(value);
  const actualKeys = Object.keys(descriptors).sort();
  if (prototype !== null && prototype !== Object.prototype
      || actualKeys.length !== keys.length
      || actualKeys.some((key, index) => key !== keys[index])
      || Object.values(descriptors).some(descriptor => !descriptor.enumerable || !("value" in descriptor))) {
    throw new TypeError(`${label} shape is invalid`);
  }
}

function assertFunction(value, label) {
  if (typeof value !== "function") {
    throw new TypeError(`${label} is required`);
  }
}

function validateSignal(signal) {
  if (signal !== null && (typeof signal.aborted !== "boolean"
      || typeof signal.addEventListener !== "function"
      || typeof signal.removeEventListener !== "function")) {
    throw new TypeError("timezone sidecar signal must be an AbortSignal");
  }
}

function throwIfAborted(signal) {
  if (!signal?.aborted) return;
  const error = new Error("Timezone sidecar materialization was cancelled by the caller.");
  error.name = "AbortError";
  throw error;
}
