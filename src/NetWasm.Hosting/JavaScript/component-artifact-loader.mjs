import { createComponentArtifactPlan } from "./component-artifact-plan.mjs";

const requestKeys = new Set([
  "artifacts",
  "compileCoreModule",
  "deploymentKind",
  "hashBytes",
  "importModule",
  "readArtifact",
  "signal",
]);
const requiredRequestKeys = Object.freeze([
  "artifacts",
  "compileCoreModule",
  "deploymentKind",
  "hashBytes",
  "importModule",
  "readArtifact",
]);
const adapterNamespaceKeys = ["contractKey", "createAdapter"];
const adapterKeys = ["contractKey", "instantiate"];
const digestPattern = /^[0-9a-f]{64}$/u;

export async function loadComponentArtifacts(request = {}) {
  assertRequest(request);
  const {
    deploymentKind,
    artifacts,
    readArtifact,
    hashBytes,
    importModule,
    compileCoreModule,
    signal = null,
  } = request;
  assertFunction(readArtifact, "component artifact reader");
  assertFunction(hashBytes, "component artifact hasher");
  assertFunction(importModule, "verified JavaScript importer");
  assertFunction(compileCoreModule, "component core-module compiler");
  validateSignal(signal);
  const plan = createComponentArtifactPlan({ deploymentKind, artifacts });
  throwIfAborted(signal);

  const executableArtifacts = [plan.adapter, plan.generatedModule, ...plan.coreModules];
  const verifiedArtifacts = await Promise.all(executableArtifacts.map(async artifact => {
    throwIfAborted(signal);
    const transportBytes = await readArtifact(artifact, signal);
    throwIfAborted(signal);
    if (!(transportBytes instanceof Uint8Array)) {
      throw new TypeError("component artifact reader returned invalid bytes");
    }
    const bytes = new Uint8Array(transportBytes);
    const actualDigest = await hashBytes(new Uint8Array(bytes));
    throwIfAborted(signal);
    if (typeof actualDigest !== "string" || !digestPattern.test(actualDigest)) {
      throw new TypeError("component artifact hasher returned an invalid digest");
    }
    if (actualDigest !== artifact.sha256) {
      throw new Error("component artifact integrity check failed");
    }
    return Object.freeze({ artifact, bytes });
  }));

  throwIfAborted(signal);
  const [adapterArtifact, generatedArtifact, ...coreArtifacts] = verifiedArtifacts;
  const loadedValues = await Promise.all([
    importModule(new Uint8Array(adapterArtifact.bytes), adapterArtifact.artifact),
    importModule(new Uint8Array(generatedArtifact.bytes), generatedArtifact.artifact),
    ...coreArtifacts.map(value => compileCoreModule(
      new Uint8Array(value.bytes),
      value.artifact)),
  ]);
  throwIfAborted(signal);

  const [adapterNamespace, generatedNamespace, ...compiledCoreModules] = loadedValues;
  validateAdapterNamespace(adapterNamespace);
  const adapter = adapterNamespace.createAdapter(generatedNamespace);
  validateAdapter(adapter);
  const coreModulesByPath = new Map();
  for (let index = 0; index < coreArtifacts.length; index++) {
    const compiled = compiledCoreModules[index];
    if (compiled === null || typeof compiled !== "object") {
      throw new TypeError("component core-module compiler returned an invalid module");
    }
    coreModulesByPath.set(coreArtifacts[index].artifact.relativePath, compiled);
  }

  return Object.freeze({
    adapter,
    loadCoreModule(name) {
      const artifact = plan.resolveCoreModule(name);
      return coreModulesByPath.get(artifact.relativePath);
    },
  });
}

function assertRequest(request) {
  if (request === null || typeof request !== "object" || Array.isArray(request)
      || Object.getOwnPropertySymbols(request).length !== 0) {
    throw new TypeError("component artifact loading request is invalid");
  }
  const prototype = Object.getPrototypeOf(request);
  const descriptors = Object.getOwnPropertyDescriptors(request);
  const keys = Object.keys(descriptors);
  if (prototype !== null && prototype !== Object.prototype
      || keys.some(key => !requestKeys.has(key))
      || requiredRequestKeys.some(key => !Object.hasOwn(descriptors, key))
      || Object.values(descriptors).some(descriptor => !descriptor.enumerable || !("value" in descriptor))) {
    throw new TypeError("component artifact loading request shape is invalid");
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
    throw new TypeError("component artifact signal must be an AbortSignal");
  }
}

function throwIfAborted(signal) {
  if (!signal?.aborted) return;
  const error = new Error("Component artifact loading was cancelled by the caller.");
  error.name = "AbortError";
  throw error;
}

function validateAdapterNamespace(namespace) {
  if (namespace === null || typeof namespace !== "object") {
    throw new TypeError("verified component adapter module is invalid");
  }
  const keys = Object.keys(namespace).sort();
  if (keys.length !== adapterNamespaceKeys.length
      || keys.some((key, index) => key !== adapterNamespaceKeys[index])
      || typeof namespace.contractKey !== "string" || namespace.contractKey.length === 0
      || typeof namespace.createAdapter !== "function") {
    throw new TypeError("verified component adapter module shape is invalid");
  }
}

function validateAdapter(adapter) {
  if (adapter === null || typeof adapter !== "object" || Array.isArray(adapter)) {
    throw new TypeError("component adapter factory returned an invalid adapter");
  }
  const keys = Object.keys(adapter).sort();
  if (keys.length !== adapterKeys.length
      || keys.some((key, index) => key !== adapterKeys[index])
      || typeof adapter.contractKey !== "string" || adapter.contractKey.length === 0
      || typeof adapter.instantiate !== "function") {
    throw new TypeError("component adapter factory returned an invalid shape");
  }
}
