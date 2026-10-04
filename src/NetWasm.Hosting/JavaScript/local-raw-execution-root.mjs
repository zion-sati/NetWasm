import { createLocalExecutionComposition } from "./local-execution-composition.mjs";
import { createLocalRawLoader } from "./local-raw-loader.mjs";
import { createManagedExceptionOutput } from "./managed-exception-output.mjs";
import { createRawExecutionStrategy } from "./raw-execution-strategy.mjs";
import { prepareRawNetWasmInterop } from "./raw-interop-preparation.mjs";
import { createSelectedArtifactStrategyResolver } from "./selected-artifact-strategy-resolver.mjs";

export function createLocalRawNetWasmExecution(options) {
  return createLocalExecutionComposition(
    options,
    value => createSelectedArtifactStrategyResolver(
      "raw",
      createRawExecutionStrategy(createLocalRawLoader({
        manifestPath: value.manifestPath,
      }))),
    ({ consumerModules, diagnosticArtifacts, manifest, stackTraceSymbols, stderr, observeAsyncCompletion, assertAsyncDeliveryAvailable }) => prepareRawNetWasmInterop({
      observeAsyncCompletion,
      assertAsyncDeliveryAvailable,
      consumerModules,
      diagnosticArtifacts,
      manifest,
      runtimeModules: Object.freeze(Object.create(null)),
      stackTraceSymbols,
      managedExceptionReporting: createManagedExceptionOutput(stderr),
    }));
}
