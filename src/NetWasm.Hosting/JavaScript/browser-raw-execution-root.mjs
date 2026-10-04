import { createBrowserExecutionComposition } from "./browser-execution-composition.mjs";
import { createBrowserRawLoader } from "./browser-raw-loader.mjs";
import { createManagedExceptionOutput } from "./managed-exception-output.mjs";
import { createRawExecutionStrategy } from "./raw-execution-strategy.mjs";
import { prepareRawNetWasmInterop } from "./raw-interop-preparation.mjs";
import { createSelectedArtifactStrategyResolver } from "./selected-artifact-strategy-resolver.mjs";

export function createBrowserRawNetWasmExecution(options, applicationModules = {}) {
  return createBrowserExecutionComposition(
    options,
    value => createSelectedArtifactStrategyResolver(
      "raw",
      createRawExecutionStrategy(createBrowserRawLoader({
        manifestUrl: value.manifestUrl,
        platform: rawPlatform(value.platform),
      }))),
    ({ consumerModules, diagnosticArtifacts, manifest, stackTraceSymbols, stderr, observeAsyncCompletion, assertAsyncDeliveryAvailable }) => prepareRawNetWasmInterop({
      observeAsyncCompletion,
      assertAsyncDeliveryAvailable,
      consumerModules: bindApplicationModules(applicationModules, consumerModules),
      diagnosticArtifacts,
      manifest,
      runtimeModules: Object.freeze(Object.create(null)),
      stackTraceSymbols,
      managedExceptionReporting: createManagedExceptionOutput(stderr),
    }));
}

function bindApplicationModules(applicationModules, providerModules) {
  const names = Object.keys(applicationModules);
  if (names.some(name => Object.hasOwn(providerModules, name))) {
    throw new TypeError("Browser application and provider module bindings overlap.");
  }
  return Object.freeze({ ...applicationModules, ...providerModules });
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
