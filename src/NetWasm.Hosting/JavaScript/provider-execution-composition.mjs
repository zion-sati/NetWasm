import { createApplicationProviderLoader } from "./application-provider-loader.mjs";
import { createCapabilityBindingValidator } from "./capability-binding-validator.mjs";
import { createContractError } from "./contract-error.mjs";
import { validateDeploymentManifest } from "./deployment-manifest-validator.mjs";
import { createExecutionRequestValidator } from "./execution-request-validator.mjs";
import { isInternalImport } from "./internal-import-policy.mjs";
import {
  createPreview2PlatformProviderRegistry,
} from "./preview2-platform-provider-registry.mjs";
import {
  createProviderExecutionPreparation,
} from "./provider-execution-preparation.mjs";
import { selectProviderKind } from "./provider-kind-selector.mjs";
import { createProviderMetadataSelector } from "./provider-metadata-selector.mjs";
import { createProviderMetadataValidator } from "./provider-metadata-validator.mjs";
import { projectProviderModuleIdentity } from "./provider-module-identity.mjs";
import { createProviderModuleProjector } from "./provider-module-projector.mjs";
import { createPlatformProviderLoader } from "./platform-provider-loader.mjs";

const factoryKeys = [
  "createShim",
  "hashBytes",
  "importModule",
  "isAbsoluteHostPath",
  "readArtifact",
];

export function createProviderExecutionComposition(options) {
  assertExactDataObject(options, factoryKeys, "provider execution composition options");
  for (const [name, value] of Object.entries(options)) {
    if (typeof value !== "function") {
      throw new TypeError(`provider execution composition '${name}' action is required`);
    }
  }

  const validateProviderMetadata = createProviderMetadataValidator({ selectProviderKind });
  const registry = createPreview2PlatformProviderRegistry({
    createShim: options.createShim,
    validateProviderMetadata,
  });
  const validateRequest = createExecutionRequestValidator({
    isAbsoluteHostPath: options.isAbsoluteHostPath,
    selectProviderKind,
  });
  return createProviderExecutionPreparation({
    loadApplicationProviders: createApplicationProviderLoader({
      hashBytes: options.hashBytes,
      importModule: options.importModule,
      readArtifact: options.readArtifact,
    }),
    loadPlatformProviders: createPlatformProviderLoader({
      resolvePlatformProvider: registry.resolve,
    }),
    projectProviderModules: createProviderModuleProjector({
      projectIdentity: projectProviderModuleIdentity,
    }),
    selectProviderMetadata: asContractAction(createProviderMetadataSelector({
      isInternalImport,
      resolvePlatformProvider: registry.resolve,
      selectProviderKind,
    })),
    validateCapabilityBinding: asContractAction(createCapabilityBindingValidator({
      isInternalImport,
      selectProviderKind,
      validateManifest: validateDeploymentManifest,
      validateProviderMetadata,
      validateRequest,
    })),
    validateManifest: asContractAction(validateDeploymentManifest),
    validateRequest: asContractAction(validateRequest),
  });
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
