import { createRawArtifactPlan } from "./raw-artifact-plan.mjs";

const requestKeys = new Set([
  "artifacts",
  "compileModule",
  "deploymentKind",
  "hashBytes",
  "importModule",
  "readArtifact",
  "signal",
]);
const requiredRequestKeys = Object.freeze([
  "artifacts",
  "compileModule",
  "deploymentKind",
  "hashBytes",
  "importModule",
  "readArtifact",
]);
const adapterNamespaceKeys = ["createAdapter", "rawAdapterMetadata"];
const layoutKeys = [
  "applicationStaticDataEnd",
  "managedExecutableEntryPoint",
  "schemaVersion",
  "target",
];
const entryPointKeys = ["completionShape", "parameterShape", "returnShape"];
const digestPattern = /^[0-9a-f]{64}$/u;
const decoder = new TextDecoder("utf-8", { fatal: true });

export async function loadRawArtifacts(request = {}) {
  assertRequest(request);
  const {
    deploymentKind,
    artifacts,
    readArtifact,
    hashBytes,
    importModule,
    compileModule,
    signal = null,
  } = request;
  assertFunction(readArtifact, "raw artifact reader");
  assertFunction(hashBytes, "raw artifact hasher");
  assertFunction(importModule, "verified raw adapter importer");
  assertFunction(compileModule, "raw module compiler");
  validateSignal(signal);
  const plan = createRawArtifactPlan({ deploymentKind, artifacts });
  throwIfAborted(signal);

  const plannedArtifacts = [
    plan.application,
    plan.adapter,
    plan.runtimeLayout,
    plan.interopManifest,
  ];
  const verifiedArtifacts = await Promise.all(plannedArtifacts.map(async artifact => {
    throwIfAborted(signal);
    const transportBytes = await readArtifact(artifact, signal);
    throwIfAborted(signal);
    if (!(transportBytes instanceof Uint8Array)) {
      throw new TypeError("raw artifact reader returned invalid bytes");
    }
    const bytes = new Uint8Array(transportBytes);
    const actualDigest = await hashBytes(new Uint8Array(bytes));
    throwIfAborted(signal);
    if (typeof actualDigest !== "string" || !digestPattern.test(actualDigest)) {
      throw new TypeError("raw artifact hasher returned an invalid digest");
    }
    if (actualDigest !== artifact.sha256) {
      throw new Error("raw artifact integrity check failed");
    }
    return Object.freeze({ artifact, bytes });
  }));

  throwIfAborted(signal);
  const [applicationArtifact, adapterArtifact, layoutArtifact, interopArtifact] = verifiedArtifacts;
  const runtimeLayout = parseRuntimeLayout(layoutArtifact.bytes);
  const interopManifest = parseInteropManifest(interopArtifact.bytes, runtimeLayout.target);
  const loadedValues = await Promise.all([
    compileModule(new Uint8Array(applicationArtifact.bytes), applicationArtifact.artifact),
    importModule(new Uint8Array(adapterArtifact.bytes), adapterArtifact.artifact),
  ]);
  throwIfAborted(signal);
  const [module, adapter] = loadedValues;
  const inventory = inspectModule(module);
  validateAdapterNamespace(adapter);
  const abi = Object.freeze({
    target: runtimeLayout.target,
    entryPoint: runtimeLayout.managedExecutableEntryPoint,
    imports: inventory.imports,
    exports: inventory.exports,
  });

  return Object.freeze({ abi, adapter, interopManifest, module, runtimeLayout });
}

function inspectModule(module) {
  let imports;
  let exports;
  try {
    imports = WebAssembly.Module.imports(module);
    exports = WebAssembly.Module.exports(module);
  } catch {
    throw new TypeError("raw module compiler returned an invalid module");
  }
  return projectRawModuleInventory(imports, exports);
}

export function projectRawModuleInventory(imports, exports) {
  const projectedImports = [];
  const importKindsByModule = new Map();
  for (const value of imports) {
    let importKinds = importKindsByModule.get(value.module);
    if (importKinds === undefined) {
      importKinds = new Map();
      importKindsByModule.set(value.module, importKinds);
    }
    const existingKind = importKinds.get(value.name);
    if (existingKind !== undefined) {
      if (existingKind !== value.kind) {
        throw new TypeError("raw module import identity has conflicting kinds");
      }
      continue;
    }
    importKinds.set(value.name, value.kind);
    projectedImports.push(Object.freeze({
      module: value.module,
      name: value.name,
      kind: value.kind,
    }));
  }
  return Object.freeze({
    imports: Object.freeze(projectedImports),
    exports: Object.freeze(exports.map(value => Object.freeze({
      name: value.name,
      kind: value.kind,
    }))),
  });
}

function validateAdapterNamespace(namespace) {
  if (namespace === null || typeof namespace !== "object") {
    throw new TypeError("verified raw adapter module is invalid");
  }
  const descriptors = Object.getOwnPropertyDescriptors(namespace);
  const keys = Object.keys(descriptors).sort();
  if (keys.length !== adapterNamespaceKeys.length
      || keys.some((key, index) => key !== adapterNamespaceKeys[index])
      || Object.values(descriptors).some(descriptor => !("value" in descriptor))
      || typeof namespace.createAdapter !== "function"
      || namespace.rawAdapterMetadata === null
      || typeof namespace.rawAdapterMetadata !== "object") {
    throw new TypeError("verified raw adapter module shape is invalid");
  }
}

function parseRuntimeLayout(bytes) {
  const value = parseJson(bytes, "runtime layout");
  assertExactDataObject(value, layoutKeys, "raw runtime layout");
  if (value.schemaVersion !== 2 || value.target !== "wasm32" && value.target !== "wasm64"
      || !Number.isSafeInteger(value.applicationStaticDataEnd) || value.applicationStaticDataEnd < 0) {
    throw new TypeError("raw runtime layout is unsupported");
  }
  assertExactDataObject(value.managedExecutableEntryPoint, entryPointKeys, "raw entry point");
  return deepFreeze(value);
}

function parseInteropManifest(bytes, target) {
  const value = parseJson(bytes, "interop manifest");
  if (value === null || typeof value !== "object" || Array.isArray(value)
      || value.version !== 1 || value.target !== target) {
    throw new TypeError("raw interop manifest does not match the runtime layout");
  }
  return deepFreeze(value);
}

function parseJson(bytes, label) {
  try {
    return JSON.parse(decoder.decode(bytes));
  } catch {
    throw new TypeError(`raw ${label} JSON is invalid`);
  }
}

function deepFreeze(value) {
  if (value !== null && typeof value === "object" && !Object.isFrozen(value)) {
    for (const child of Object.values(value)) deepFreeze(child);
    Object.freeze(value);
  }
  return value;
}

function assertRequest(request) {
  if (request === null || typeof request !== "object" || Array.isArray(request)
      || Object.getOwnPropertySymbols(request).length !== 0) {
    throw new TypeError("raw artifact loading request is invalid");
  }
  const prototype = Object.getPrototypeOf(request);
  const descriptors = Object.getOwnPropertyDescriptors(request);
  const keys = Object.keys(descriptors);
  if (prototype !== null && prototype !== Object.prototype
      || keys.some(key => !requestKeys.has(key))
      || requiredRequestKeys.some(key => !Object.hasOwn(descriptors, key))
      || Object.values(descriptors).some(descriptor => !descriptor.enumerable || !("value" in descriptor))) {
    throw new TypeError("raw artifact loading request shape is invalid");
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
  if (typeof value !== "function") throw new TypeError(`${label} is required`);
}

function validateSignal(signal) {
  if (signal !== null && (typeof signal.aborted !== "boolean"
      || typeof signal.addEventListener !== "function"
      || typeof signal.removeEventListener !== "function")) {
    throw new TypeError("raw artifact signal must be an AbortSignal");
  }
}

function throwIfAborted(signal) {
  if (!signal?.aborted) return;
  const error = new Error("Raw artifact loading was cancelled by the caller.");
  error.name = "AbortError";
  throw error;
}
