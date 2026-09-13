import { createBrowserArtifactTransport } from "./browser-artifact-transport.mjs";
import { createBrowserDeploymentManifestLoader } from "./browser-deployment-manifest-loader.mjs";
import { createBrowserTimeZoneMaterializer } from "./browser-timezone-materializer.mjs";
import { createBrowserVerifiedModuleImporter } from "./browser-verified-module-importer.mjs";
import { createContractError } from "./contract-error.mjs";
import { createDeploymentExecution } from "./deployment-execution.mjs";
import { createExecutionRequestValidator } from "./execution-request-validator.mjs";
import { createNetWasmExecutionFacade } from "./netwasm-execution-facade.mjs";
import { createOutputFinalization } from "./output-finalization.mjs";
import { createPreview2FilesystemScope } from "./preview2-filesystem-scope.mjs";
import { createProviderExecutionComposition } from "./provider-execution-composition.mjs";
import { selectProviderKind } from "./provider-kind-selector.mjs";
import { executeWithTimeZoneResources } from "./timezone-execution.mjs";
import { selectTimeZoneSidecar } from "./timezone-sidecar-selector.mjs";

const factoryKeys = ["manifestUrl", "platform"];
const platformKeys = [
  "compileCoreModule",
  "createFilesystem",
  "createModuleUrl",
  "createShim",
  "digest",
  "fetch",
  "importModule",
  "revokeModuleUrl",
];

export function createBrowserExecutionComposition(
  options,
  createStrategyResolver,
  prepareRawInterop) {
  validateOptions(options);
  if (typeof createStrategyResolver !== "function"
      || prepareRawInterop !== null && typeof prepareRawInterop !== "function") {
    throw new TypeError("browser NetWasm execution composition is invalid");
  }
  const transport = createBrowserArtifactTransport({
    digest: options.platform.digest,
    fetch: options.platform.fetch,
    manifestUrl: options.manifestUrl,
  });
  const importModule = createBrowserVerifiedModuleImporter({
    createModuleUrl: options.platform.createModuleUrl,
    importModule: options.platform.importModule,
    revokeModuleUrl: options.platform.revokeModuleUrl,
  });
  const prepareProviders = createProviderExecutionComposition({
    createShim: options.platform.createShim,
    hashBytes: transport.hashBytes,
    importModule,
    isAbsoluteHostPath: rejectHostPath,
    readArtifact: transport.readArtifact,
  });
  const validateRequest = createExecutionRequestValidator({
    isAbsoluteHostPath: rejectHostPath,
    selectProviderKind,
  });
  const resolveStrategy = createStrategyResolver(options);
  if (typeof resolveStrategy !== "function") {
    throw new TypeError("browser NetWasm execution Strategy resolver is invalid");
  }
  const executeDeployment = createDeploymentExecution({
    createFilesystem: preopens => createPreview2FilesystemScope({
      createFilesystem: options.platform.createFilesystem,
      preopens,
    }),
    createOutputFinalization,
    createTimeZoneMaterializer: mountReadOnlyFile => createBrowserTimeZoneMaterializer({
      manifestUrl: options.manifestUrl,
      platform: Object.freeze({
        digest: options.platform.digest,
        fetch: options.platform.fetch,
        mountReadOnlyFile,
      }),
    }),
    executeWithResources: executeWithTimeZoneResources,
    loadManifest: createBrowserDeploymentManifestLoader({
      manifestUrl: options.manifestUrl,
      platform: Object.freeze({
        digest: options.platform.digest,
        fetch: options.platform.fetch,
      }),
    }),
    prepareProviders,
    prepareRawInterop,
    resolveStrategy: kind => asContractStrategy(resolveStrategy, kind),
    selectTimeZone: selectTimeZoneSidecar,
    validateRequest: asContractAction(validateRequest),
  });
  return createNetWasmExecutionFacade(executeDeployment);
}

function asContractStrategy(resolve, kind) {
  try {
    return resolve(kind);
  } catch {
    throw createContractError();
  }
}

function rejectHostPath() {
  return false;
}

function asContractAction(action) {
  return value => {
    try {
      return action(value);
    } catch {
      throw createContractError();
    }
  };
}

function validateOptions(options) {
  assertExactDataObject(options, factoryKeys, "browser NetWasm execution options");
  assertExactDataObject(options.platform, platformKeys, "browser NetWasm execution platform");
  for (const [name, value] of Object.entries(options.platform)) {
    if (typeof value !== "function") {
      throw new TypeError(`browser NetWasm execution platform '${name}' action is required`);
    }
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
      || Object.values(descriptors).some(
        descriptor => !descriptor.enumerable || !("value" in descriptor))) {
    throw new TypeError(`${label} shape is invalid`);
  }
}
