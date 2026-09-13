import { createComponentExecutionStrategy } from "./component-execution-strategy.mjs";
import { createLocalComponentLoader } from "./local-component-loader.mjs";
import { createLocalExecutionComposition } from "./local-execution-composition.mjs";
import { createSelectedArtifactStrategyResolver } from "./selected-artifact-strategy-resolver.mjs";

export function createLocalComponentNetWasmExecution(options) {
  return createLocalExecutionComposition(
    options,
    value => createSelectedArtifactStrategyResolver(
      "component",
      createComponentExecutionStrategy(createLocalComponentLoader({
        manifestPath: value.manifestPath,
      }))),
    null);
}
