import { createBrowserExecutionComposition } from "./browser-execution-composition.mjs";
import { createBrowserRawLoader } from "./browser-raw-loader.mjs";
import { createManagedExceptionOutput } from "./managed-exception-output.mjs";
import { createRawExecutionStrategy } from "./raw-execution-strategy.mjs";
import { prepareRawNetWasmInterop } from "./raw-interop-preparation.mjs";
import { createSelectedArtifactStrategyResolver } from "./selected-artifact-strategy-resolver.mjs";

export function createBrowserRawNetWasmExecution(options) {
  return createBrowserExecutionComposition(
    options,
    value => createSelectedArtifactStrategyResolver(
      "raw",
      createRawExecutionStrategy(createBrowserRawLoader({
        manifestUrl: value.manifestUrl,
        platform: rawPlatform(value.platform),
      }))),
    ({ consumerModules, manifest, stderr }) => prepareRawNetWasmInterop({
      consumerModules,
      manifest,
      runtimeModules: Object.freeze(Object.create(null)),
      managedExceptionReporting: createManagedExceptionOutput(stderr),
    }));
}

function rawPlatform(platform) {
  return Object.freeze({
    compileCoreModule: platform.compileCoreModule,
    createModuleUrl: platform.createModuleUrl,
    digest: platform.digest,
    fetch: platform.fetch,
    importModule: platform.importModule,
    revokeModuleUrl: platform.revokeModuleUrl,
  });
}
