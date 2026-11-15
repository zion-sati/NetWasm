// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: MIT

import { createBrowserComponentLoader } from "./browser-component-loader.mjs";
import { createBrowserExecutionComposition } from "./browser-execution-composition.mjs";
import { createComponentExecutionStrategy } from "./component-execution-strategy.mjs";
import { createSelectedArtifactStrategyResolver } from "./selected-artifact-strategy-resolver.mjs";

export function createBrowserComponentNetWasmExecution(options) {
  return createBrowserExecutionComposition(
    options,
    value => createSelectedArtifactStrategyResolver(
      "browser",
      createComponentExecutionStrategy(createBrowserComponentLoader({
        manifestUrl: value.manifestUrl,
        platform: componentPlatform(value.platform),
      }))),
    null);
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
