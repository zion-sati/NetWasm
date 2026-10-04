import { createLocalArtifactStrategyRegistry } from "./local-artifact-strategy-registry.mjs";
import { createLocalExecutionComposition } from "./local-execution-composition.mjs";
import { createManagedExceptionOutput } from "./managed-exception-output.mjs";
import { prepareRawNetWasmInterop } from "./raw-interop-preparation.mjs";

export function createLocalNetWasmExecution(options) {
  return createLocalExecutionComposition(
    options,
    value => {
      const registry = createLocalArtifactStrategyRegistry({
        manifestPath: value.manifestPath,
      });
      return kind => registry.resolve(kind);
    },
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
