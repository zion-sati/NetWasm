import { createBrowserArtifactStrategyRegistry } from "./browser-artifact-strategy-registry.mjs";
import { createBrowserExecutionComposition } from "./browser-execution-composition.mjs";
import { createManagedExceptionOutput } from "./managed-exception-output.mjs";
import { prepareRawNetWasmInterop } from "./raw-interop-preparation.mjs";

export function createBrowserNetWasmExecution(options) {
  return createBrowserExecutionComposition(
    options,
    value => {
      const registry = createBrowserArtifactStrategyRegistry({
        manifestUrl: value.manifestUrl,
        platform: componentPlatform(value.platform),
      });
      return kind => registry.resolve(kind);
    },
    ({ consumerModules, manifest, stderr }) => prepareRawNetWasmInterop({
      consumerModules,
      manifest,
      runtimeModules: Object.freeze(Object.create(null)),
      managedExceptionReporting: createManagedExceptionOutput(stderr),
    }));
}

function componentPlatform(platform) {
  return Object.freeze({
    compileCoreModule: platform.compileCoreModule,
    createModuleUrl: platform.createModuleUrl,
    digest: platform.digest,
    fetch: platform.fetch,
    importModule: platform.importModule,
    revokeModuleUrl: platform.revokeModuleUrl,
  });
}
