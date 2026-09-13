import { createArtifactStrategyRegistry } from "./artifact-strategy-registry.mjs";
import { createBrowserComponentLoader } from "./browser-component-loader.mjs";
import { createBrowserRawLoader } from "./browser-raw-loader.mjs";
import { createComponentExecutionStrategy } from "./component-execution-strategy.mjs";
import { createRawExecutionStrategy } from "./raw-execution-strategy.mjs";

export function createBrowserArtifactStrategyRegistry(options) {
  const executeComponent = createComponentExecutionStrategy(
    createBrowserComponentLoader(options));
  const executeRaw = createRawExecutionStrategy(createBrowserRawLoader(options));
  return createArtifactStrategyRegistry([
    { kind: "browser", execute: executeComponent },
    { kind: "raw", execute: executeRaw },
  ]);
}
