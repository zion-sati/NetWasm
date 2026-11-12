import { snapshotDeploymentArtifact } from "./deployment-artifact.mjs";
import { readOptionalSymbolSidecar } from "./optional-symbol-sidecars.mjs";
import { readTimeZoneNames } from "./timezone-sidecar-zones.mjs";

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
const selectionKeys = ["artifacts", "timeZone"];
const digestPattern = /^[0-9a-f]{64}$/u;
// Every sidecar is mounted read-only under one directory, by its own file name (issue 225).
const mountPath = "/netwasm-sidecars";
const symbolSidecars = Object.freeze({
  "type-symbols": "application/vnd.netwasm.type-symbols;version=1",
  "method-symbols": "application/vnd.netwasm.method-symbols;version=1",
});

export async function materializeProgramSidecars(request = {}) {
  assertRequest(request);
  const {
    selection,
    readArtifact,
    hashBytes,
    mountReadOnlyFile,
    signal = null,
  } = request;
  assertFunction(readArtifact, "program sidecar reader");
  assertFunction(hashBytes, "program sidecar hasher");
  assertFunction(mountReadOnlyFile, "program sidecar read-only mount act");
  validateSignal(signal);
  if (selection === null) return null;

  const selected = snapshotSelection(selection);
  const releases = [];
  const release = () => {
    for (const action of releases.splice(0).reverse()) action();
  };
  try {
    for (const artifact of selected.artifacts) {
      const verify = value => readVerified(value, readArtifact, hashBytes, signal);
      // A symbols file that cannot be read or verified is not mounted; the program then reads
      // type#<id> and method#<id> and says so on standard error (issue 225). Time zone data is required.
      const bytes = artifact.role === "timezone-data"
        ? await verify(artifact)
        : await readOptionalSymbolSidecar(artifact, verify, signal);
      if (bytes === undefined) {
        continue;
      }
      // Reject an unselected zone here, with a fix, rather than as a startup trap.
      if (artifact.role === "timezone-data" && !readTimeZoneNames(bytes).has(selected.timeZone)) {
        throw new TypeError(`TZ '${selected.timeZone}' is not in this deployment's time zone data. `
          + "Add it to the project's NetWasmTimeZone items, or set NetWasmTimeZoneMode to Full.");
      }
      const name = artifact.relativePath.slice(artifact.relativePath.lastIndexOf("/") + 1);
      const releaseMount = mountReadOnlyFile(Object.freeze({
        guestPath: `${mountPath}/${name}`,
        bytes: new Uint8Array(bytes),
      }));
      if (typeof releaseMount !== "function") {
        throw new TypeError("program sidecar read-only mount act returned an invalid release action");
      }
      releases.push(releaseMount);
    }
  } catch (error) {
    release();
    throw error;
  }
  return Object.freeze({
    code: "host.sidecar-mount",
    message: "The execution host could not release the program's sidecar mounts.",
    release,
  });
}

async function readVerified(artifact, readArtifact, hashBytes, signal) {
  throwIfAborted(signal);
  const transportBytes = await readArtifact(artifact, signal);
  throwIfAborted(signal);
  if (!(transportBytes instanceof Uint8Array)) {
    throw new TypeError("program sidecar reader returned invalid bytes");
  }
  const bytes = new Uint8Array(transportBytes);
  const actualDigest = await hashBytes(new Uint8Array(bytes));
  throwIfAborted(signal);
  if (typeof actualDigest !== "string" || !digestPattern.test(actualDigest)) {
    throw new TypeError("program sidecar hasher returned an invalid digest");
  }
  if (actualDigest !== artifact.sha256) {
    throw new Error(`program sidecar ${artifact.role} integrity check failed`);
  }
  return bytes;
}

function snapshotSelection(value) {
  assertExactDataObject(value, selectionKeys, "program sidecar selection");
  if (value.timeZone !== null && (typeof value.timeZone !== "string" || value.timeZone.length === 0
      || value.timeZone.includes("\0") || value.timeZone === "UTC"
      || value.timeZone === "Etc/UTC")) {
    throw new TypeError("program sidecar selection requires a non-UTC TZ or none");
  }
  if (!Array.isArray(value.artifacts) || value.artifacts.length === 0) {
    throw new TypeError("program sidecar selection requires artifacts");
  }
  const artifacts = value.artifacts.map(snapshotDeploymentArtifact);
  for (const artifact of artifacts) {
    const valid = artifact.role === "timezone-data"
      ? value.timeZone !== null
        && artifact.mediaType === "application/octet-stream"
        && artifact.relativePath.endsWith(".wasm.tz-info")
      : Object.hasOwn(symbolSidecars, artifact.role)
        && artifact.mediaType === symbolSidecars[artifact.role]
        && artifact.relativePath.endsWith(`.wasm.${artifact.role}`);
    if (!valid || artifact.schemaVersion !== 1) {
      throw new TypeError("program sidecar selection has an invalid artifact contract");
    }
  }
  return Object.freeze({ timeZone: value.timeZone, artifacts: Object.freeze(artifacts) });
}

function assertRequest(request) {
  if (request === null || typeof request !== "object" || Array.isArray(request)
      || Object.getOwnPropertySymbols(request).length !== 0) {
    throw new TypeError("program sidecar materialization request is invalid");
  }
  const prototype = Object.getPrototypeOf(request);
  const descriptors = Object.getOwnPropertyDescriptors(request);
  const keys = Object.keys(descriptors);
  if (prototype !== null && prototype !== Object.prototype
      || keys.some(key => !requestKeys.has(key))
      || requiredRequestKeys.some(key => !Object.hasOwn(descriptors, key))
      || Object.values(descriptors).some(descriptor => !descriptor.enumerable || !("value" in descriptor))) {
    throw new TypeError("program sidecar materialization request shape is invalid");
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
    throw new TypeError("program sidecar signal must be an AbortSignal");
  }
}

function throwIfAborted(signal) {
  if (!signal?.aborted) return;
  const error = new Error("Program sidecar materialization was cancelled by the caller.");
  error.name = "AbortError";
  throw error;
}
