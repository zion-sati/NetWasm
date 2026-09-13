import { isAbsolute } from "node:path";

import { createContractError } from "./contract-error.mjs";
import { createDeploymentExecution } from "./deployment-execution.mjs";
import { createExecutionRequestValidator } from "./execution-request-validator.mjs";
import { createLocalArtifactTransport } from "./local-artifact-transport.mjs";
import { createLocalDeploymentManifestLoader } from "./local-deployment-manifest-loader.mjs";
import { createLocalTimeZoneMaterializer } from "./local-timezone-materializer.mjs";
import { importLocalVerifiedModule } from "./local-verified-module-importer.mjs";
import { createNetWasmExecutionFacade } from "./netwasm-execution-facade.mjs";
import { createOutputFinalization } from "./output-finalization.mjs";
import { createPreview2FilesystemScope } from "./preview2-filesystem-scope.mjs";
import { createProviderExecutionComposition } from "./provider-execution-composition.mjs";
import { selectProviderKind } from "./provider-kind-selector.mjs";
import { executeWithTimeZoneResources } from "./timezone-execution.mjs";
import { selectTimeZoneSidecar } from "./timezone-sidecar-selector.mjs";

const factoryKeys = ["manifestPath", "platform"];
const platformKeys = ["createFilesystem", "createShim"];

export function createLocalExecutionComposition(
  options,
  createStrategyResolver,
  prepareRawInterop) {
  validateOptions(options);
  if (typeof createStrategyResolver !== "function"
      || prepareRawInterop !== null && typeof prepareRawInterop !== "function") {
    throw new TypeError("local NetWasm execution composition is invalid");
  }
  const transport = createLocalArtifactTransport({ manifestPath: options.manifestPath });
  const prepareProviders = createProviderExecutionComposition({
    createShim: options.platform.createShim,
    hashBytes: transport.hashBytes,
    importModule: importLocalVerifiedModule,
    isAbsoluteHostPath: isAbsolute,
    readArtifact: transport.readArtifact,
  });
  const validateRequest = createExecutionRequestValidator({
    isAbsoluteHostPath: isAbsolute,
    selectProviderKind,
  });
  const resolveStrategy = createStrategyResolver(options);
  if (typeof resolveStrategy !== "function") {
    throw new TypeError("local NetWasm execution Strategy resolver is invalid");
  }
  const executeDeployment = createDeploymentExecution({
    createFilesystem: preopens => createPreview2FilesystemScope({
      createFilesystem: options.platform.createFilesystem,
      preopens,
    }),
    createOutputFinalization,
    createTimeZoneMaterializer: mountReadOnlyFile => createLocalTimeZoneMaterializer({
      manifestPath: options.manifestPath,
      mountReadOnlyFile,
    }),
    executeWithResources: executeWithTimeZoneResources,
    loadManifest: createLocalDeploymentManifestLoader({ manifestPath: options.manifestPath }),
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
  assertExactDataObject(options, factoryKeys, "local NetWasm execution options");
  assertExactDataObject(options.platform, platformKeys, "local NetWasm execution platform");
  for (const [name, value] of Object.entries(options.platform)) {
    if (typeof value !== "function") {
      throw new TypeError(`local NetWasm execution platform '${name}' action is required`);
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
