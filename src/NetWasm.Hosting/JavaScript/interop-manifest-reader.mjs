import {
  isAbiType,
  isHandleValueAbiType,
  isMemoryViewAbiType,
  isScalarAbiType,
  nullableUnderlying,
  isTransferAbiType,
  isValueArrayAbiType,
} from "./interop-abi-types.mjs";
import { NetWasmHostError } from "./managed-errors.mjs";

const hostModule = "netwasm.host.v1";

const targetLayouts = Object.freeze({
  wasm32: Object.freeze({
    managedReferenceSize: 4,
    stringLengthOffset: 4,
    stringDataOffset: 8,
    arrayLengthOffset: 4,
    arrayDataPointerOffset: 8,
    spanReferenceOffset: 0,
    spanLengthOffset: 4,
  }),
  wasm64: Object.freeze({
    managedReferenceSize: 8,
    stringLengthOffset: 8,
    stringDataOffset: 12,
    arrayLengthOffset: 8,
    arrayDataPointerOffset: 16,
    spanReferenceOffset: 0,
    spanLengthOffset: 8,
  }),
});

export function parseInteropManifest(manifest) {
  // Version 2 calls scalar JSImports directly.
  if (manifest == null || manifest.version !== 2) {
    throw new NetWasmHostError("unsupported NetWasm interop manifest version");
  }
  const expectedLayout = targetLayouts[manifest.target];
  if (expectedLayout === undefined) {
    throw new NetWasmHostError("interop manifest target must be wasm32 or wasm64");
  }
  validateEnvelope(manifest);
  validateTargetLayout(manifest.target, manifest.targetLayout, expectedLayout);
  validateImports(manifest.imports);
  validateExports(manifest.exports);
  validateCallbacks(manifest.imports, manifest.callbacks ?? []);
  validateImportModules(manifest.importModules ?? []);
  return manifest;
}

// The modules a host supplies: those [JSImport]s name, and those whose
// functions the final module imports unchanged ([WasmImportLinkage] imports
// and the imports of the project's native archives).
export function requiredImportModules(manifest) {
  return new Set([
    ...manifest.imports.map(descriptor => descriptor.module).filter(module => module !== hostModule),
    ...(manifest.importModules ?? []),
  ]);
}

function validateImportModules(modules) {
  if (!Array.isArray(modules)
      || modules.some(module => typeof module !== "string" || module.length === 0 || isReservedModule(module))
      || new Set(modules).size !== modules.length) {
    throw new NetWasmHostError("interop manifest contains an invalid import module");
  }
}

// NetWasm's own modules and WASI interfaces are never application imports.
function isReservedModule(module) {
  return module.startsWith("netwasm.") || module.startsWith("netwasm:")
    || module.startsWith("wasi:") || module.startsWith("wasi_");
}

function validateEnvelope(manifest) {
  if (manifest.statusAbi == null
      || manifest.statusAbi.successStatus !== 0
      || manifest.statusAbi.hostFailureStatus !== 1
      || manifest.statusAbi.scalarResultOffset !== 0
      || manifest.targetLayout == null
      || !Array.isArray(manifest.imports)
      || !Array.isArray(manifest.exports)
      || manifest.callbacks !== undefined && !Array.isArray(manifest.callbacks)) {
    throw new NetWasmHostError(
      "interop manifest must contain imports and exports arrays");
  }
}

function validateTargetLayout(target, actual, expected) {
  for (const [name, value] of Object.entries(expected)) {
    if (!Number.isInteger(actual[name]) || actual[name] !== value) {
      throw new NetWasmHostError(
        `interop manifest has an invalid ${name} for ${target}`);
    }
  }
}

function validateImports(imports) {
  const names = new Set();
  for (const descriptor of imports) {
    validateImportDescriptor(descriptor);
    const key = `${descriptor.module}\u0000${descriptor.name}`;
    if (names.has(key)) {
      throw new NetWasmHostError(
        `duplicate host import ${descriptor.module}.${descriptor.name}`);
    }
    names.add(key);
  }
}

function validateImportDescriptor(descriptor) {
  if (descriptor == null
      || typeof descriptor.module !== "string" || descriptor.module.length === 0
      || typeof descriptor.name !== "string" || descriptor.name.length === 0
      || !Array.isArray(descriptor.parameters)
      || !isAbiType(descriptor.result)
      || descriptor.parameters.some(type => !isAbiType(type))
      || descriptor.call !== "status" && descriptor.call !== "direct") {
    throw new NetWasmHostError(
      "interop manifest contains an invalid import descriptor");
  }
  // A span view is valid only during a synchronous call, and only as an argument.
  if (isMemoryViewAbiType(descriptor.result)
      || descriptor.asyncReturn != null && descriptor.parameters.some(isMemoryViewAbiType)) {
    throw new NetWasmHostError(
      "interop manifest contains an invalid span import descriptor");
  }
  // A direct import passes primitive scalars as WebAssembly values.
  if (descriptor.call === "direct"
      && (descriptor.asyncReturn != null
        || descriptor.parameters.some(type => !isScalarAbiType(type))
        || descriptor.result !== "void" && !isScalarAbiType(descriptor.result))) {
    throw new NetWasmHostError(
      "interop manifest contains an invalid direct import descriptor");
  }
  if (descriptor.asyncReturn != null
      && (descriptor.asyncReturn !== "task" && descriptor.asyncReturn !== "value-task"
        || typeof descriptor.resolveExport !== "string" || descriptor.resolveExport.length === 0
        || typeof descriptor.rejectExport !== "string" || descriptor.rejectExport.length === 0
        || typeof descriptor.cancelExport !== "string" || descriptor.cancelExport.length === 0
        || descriptor.result !== "void" && !isValueAbiType(descriptor.result)
          && descriptor.result !== "string" && descriptor.result !== "object"
          && !isValueArrayAbiType(descriptor.result) && !isHandleValueAbiType(descriptor.result))) {
    throw new NetWasmHostError(
      "interop manifest contains an invalid async import descriptor");
  }
}

// A scalar, date, or either nullable: a value that crosses without a host handle.
function isValueAbiType(type) {
  return isScalarAbiType(type) || nullableUnderlying(type) !== null;
}

function validateExports(exports) {
  const names = new Set();
  for (const descriptor of exports) {
    validateExportDescriptor(descriptor);
    if (names.has(descriptor.name)) {
      throw new NetWasmHostError(
        `duplicate managed export ${descriptor.name}`);
    }
    names.add(descriptor.name);
  }
}

function validateExportDescriptor(descriptor) {
  if (descriptor == null
      || typeof descriptor.name !== "string" || descriptor.name.length === 0
      || !Array.isArray(descriptor.parameters)
      || !isAbiType(descriptor.result)
      || descriptor.parameters.some(type => !isValueAbiType(type) && !isExportReference(type))
      || descriptor.result !== "void" && !isValueAbiType(descriptor.result)
        && !isExportReference(descriptor.result)
      || !hasValidParameterMetadata(descriptor)) {
    throw new NetWasmHostError(
      "interop manifest contains an invalid export descriptor");
  }
  if (descriptor.asyncReturn != null
      && (descriptor.asyncReturn !== "task" && descriptor.asyncReturn !== "value-task"
        || typeof descriptor.statusExport !== "string" || descriptor.statusExport.length === 0
        || typeof descriptor.completeExport !== "string" || descriptor.completeExport.length === 0
        || descriptor.completionResult !== "exception-handle-v1"
        || descriptor.result !== "void"
          && (typeof descriptor.resultExport !== "string"
            || descriptor.resultExport.length === 0))) {
    throw new NetWasmHostError(
      "interop manifest contains an invalid async export descriptor");
  }
}

// Parameter names and nullability are optional: none at all, or one entry per
// parameter. They only shape generated declarations.
function hasValidParameterMetadata(descriptor) {
  const listed = (values, type) => values === undefined || Array.isArray(values)
    && (values.length === 0 || values.length === descriptor.parameters.length)
    && values.every(value => typeof value === type);
  return listed(descriptor.parameterNames, "string")
    && listed(descriptor.nonNullableParameters, "boolean")
    && (descriptor.nonNullableResult === undefined || typeof descriptor.nonNullableResult === "boolean");
}

// Values an export copies (strings, arrays) or wraps (JSObject) through a host handle.
function isExportReference(type) {
  return isTransferAbiType(type) || type === "object" || isHandleValueAbiType(type);
}

function validateCallbacks(imports, callbacks) {
  const callbackExports = new Set();
  for (const callback of callbacks) {
    validateCallbackDescriptor(callback);
    const importDescriptor = imports.find(candidate =>
      candidate.module === callback.module && candidate.name === callback.importName);
    if (importDescriptor == null
        || importDescriptor.parameters[callback.parameterIndex] !== "callback"
        || callbackExports.has(callback.exportName)) {
      throw new NetWasmHostError(
        "interop manifest callback does not match an import descriptor");
    }
    callbackExports.add(callback.exportName);
  }

  for (const descriptor of imports) {
    for (let index = 0; index < descriptor.parameters.length; index++) {
      if (descriptor.parameters[index] === "callback"
          && !callbacks.some(callback => callback.module === descriptor.module
            && callback.importName === descriptor.name
            && callback.parameterIndex === index)) {
        throw new NetWasmHostError(
          "interop manifest import callback has no callback descriptor");
      }
    }
  }
}

function validateCallbackDescriptor(callback) {
  if (callback == null
      || typeof callback.module !== "string" || callback.module.length === 0
      || typeof callback.importName !== "string" || callback.importName.length === 0
      || !Number.isInteger(callback.parameterIndex) || callback.parameterIndex < 0
      || typeof callback.exportName !== "string" || callback.exportName.length === 0
      || !Array.isArray(callback.parameters)
      || callback.result !== "void" && !isValueAbiType(callback.result) && !isExportReference(callback.result)
      || callback.parameters.some(type => !isValueAbiType(type) && !isExportReference(type))) {
    throw new NetWasmHostError(
      "interop manifest contains an invalid callback descriptor");
  }
}
