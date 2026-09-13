import {
  createOutputSinkStream,
  validateOutputSink,
} from "./output-sink-stream.mjs";

const factoryKeys = ["catalog", "createShim"];
const catalogKeys = ["providers", "schema", "shim", "witSha256", "world"];
const shimKeys = ["package", "version"];
const requestKeys = [
  "arguments", "environment", "filesystem", "grants", "signal", "stderr", "stdout",
];
const grantKeys = ["clocks", "environment", "network", "preopens", "randomness"];
const providerKeys = ["capability", "functions", "module"];
const shimPackage = "@bytecodealliance/preview2-shim";
const shimVersion = "0.24.1";
const world = "netwasm:hosting-platform/preview2@1.0.0";

export function createPreview2PlatformProviderRegistrations(options) {
  assertExactDataObject(options, factoryKeys, "Preview 2 registration options");
  if (typeof options.createShim !== "function") {
    throw new TypeError("Preview 2 shim creator is required");
  }
  const providers = readCatalog(options.catalog);
  const scopes = new WeakMap();
  return Object.freeze(providers.map(provider => Object.freeze({
    provider,
    createSource(request) {
      const imports = resolveImports(scopes, options.createShim, request);
      const source = imports[provider.module];
      if (source === null || typeof source !== "object" || Array.isArray(source)) {
        throw new TypeError(
          `Preview 2 shim does not provide module '${provider.module}'`);
      }
      return source;
    },
  })));
}

function resolveImports(scopes, createShim, request) {
  readFactoryRequest(request);
  const retained = scopes.get(request);
  if (retained !== undefined) return retained;
  if (request.grants.preopens.length !== 0 && request.filesystem === null) {
    throw new TypeError("granted preopens require a caller-owned filesystem");
  }

  const environment = Object.fromEntries(
    request.environment.map(variable => [variable.name, variable.value]));
  const sandboxValues = {
    args: request.arguments,
    enableNetwork: request.grants.network === "allowAll",
    env: Object.freeze(environment),
  };
  if (request.filesystem === null) {
    sandboxValues.preopens = Object.freeze({});
  }
  const sandbox = Object.freeze(sandboxValues);
  const config = request.filesystem === null
    ? Object.freeze({ sandbox })
    : Object.freeze({ filesystem: request.filesystem, sandbox });
  const shim = createShim(config);
  if (shim === null || typeof shim !== "object"
      || typeof shim.getImportObject !== "function") {
    throw new TypeError("Preview 2 shim creator returned an invalid shim");
  }
  const imports = shim.getImportObject(Object.freeze({ asVersion: "0.2.11" }));
  if (imports === null || typeof imports !== "object" || Array.isArray(imports)) {
    throw new TypeError("Preview 2 shim returned an invalid import object");
  }
  const isolatedImports = isolateOutputImports(imports, request);
  scopes.set(request, isolatedImports);
  return isolatedImports;
}

function isolateOutputImports(imports, request) {
  const streams = readImportSource(imports, "wasi:io/streams@0.2.11");
  const poll = readImportSource(imports, "wasi:io/poll@0.2.11");
  const types = {
    outputStreamType: readResourceType(streams, "OutputStream"),
    pollableType: readResourceType(poll, "Pollable"),
  };
  const createStdout = () => createOutputSinkStream({ ...types, sink: request.stdout });
  const createStderr = () => createOutputSinkStream({ ...types, sink: request.stderr });
  const result = Object.assign(Object.create(null), imports);
  result["wasi:cli/stdout@0.2.11"] = Object.freeze({ getStdout: createStdout });
  result["wasi:cli/stderr@0.2.11"] = Object.freeze({ getStderr: createStderr });
  result["wasi:cli/terminal-stdout@0.2.11"] = Object.freeze({
    getTerminalStdout: () => undefined,
  });
  result["wasi:cli/terminal-stderr@0.2.11"] = Object.freeze({
    getTerminalStderr: () => undefined,
  });
  return Object.freeze(result);
}

function readImportSource(imports, module) {
  const descriptor = Object.getOwnPropertyDescriptor(imports, module);
  if (descriptor === undefined || !("value" in descriptor)
      || descriptor.value === null || typeof descriptor.value !== "object"
      || Array.isArray(descriptor.value)) {
    throw new TypeError(`Preview 2 shim does not provide module '${module}'`);
  }
  return descriptor.value;
}

function readResourceType(source, name) {
  const descriptor = Object.getOwnPropertyDescriptor(source, name);
  if (descriptor === undefined || !("value" in descriptor)
      || typeof descriptor.value !== "function") {
    throw new TypeError(`Preview 2 shim does not provide resource '${name}'`);
  }
  return descriptor.value;
}

function readCatalog(catalog) {
  assertExactDataObject(catalog, catalogKeys, "Preview 2 platform catalog");
  assertExactDataObject(catalog.shim, shimKeys, "Preview 2 platform catalog shim");
  if (!Object.isFrozen(catalog) || catalog.schema !== 1 || catalog.world !== world
      || typeof catalog.witSha256 !== "string" || !/^[0-9a-f]{64}$/u.test(catalog.witSha256)
      || catalog.shim.package !== shimPackage || catalog.shim.version !== shimVersion
      || !Array.isArray(catalog.providers) || !Object.isFrozen(catalog.providers)
      || catalog.providers.length === 0) {
    throw new TypeError("Preview 2 platform catalog is incompatible");
  }
  for (const provider of catalog.providers) {
    assertExactDataObject(provider, providerKeys, "Preview 2 platform provider");
    if (!Object.isFrozen(provider)
        || typeof provider.module !== "string" || provider.module.length === 0
        || !Array.isArray(provider.functions) || !Object.isFrozen(provider.functions)
        || provider.functions.length === 0) {
      throw new TypeError("Preview 2 platform provider is incomplete");
    }
  }
  return catalog.providers;
}

function readFactoryRequest(request) {
  assertExactDataObject(request, requestKeys, "Preview 2 source request");
  assertExactDataObject(request.grants, grantKeys, "Preview 2 source grants");
  if (!Object.isFrozen(request)
      || !Array.isArray(request.arguments) || !Object.isFrozen(request.arguments)
      || !Array.isArray(request.environment) || !Object.isFrozen(request.environment)
      || !Object.isFrozen(request.grants)
      || !Array.isArray(request.grants.preopens)
      || request.filesystem !== null
        && (typeof request.filesystem !== "object" || Array.isArray(request.filesystem))) {
    throw new TypeError("Preview 2 source request is incomplete");
  }
  validateOutputSink(request.stdout, "Preview 2 stdout output sink");
  validateOutputSink(request.stderr, "Preview 2 stderr output sink");
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
      || Object.values(descriptors).some(
        descriptor => !descriptor.enumerable || !("value" in descriptor))) {
    throw new TypeError(`${label} shape is invalid`);
  }
}
