// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: MIT

import { createArtifactStrategyRegistry } from "./artifact-strategy-registry.mjs";
import { createComponentExecutionStrategy } from "./component-execution-strategy.mjs";
import { createLocalComponentLoader } from "./local-component-loader.mjs";
import { createLocalRawLoader } from "./local-raw-loader.mjs";
import { createRawExecutionStrategy } from "./raw-execution-strategy.mjs";

export function createLocalArtifactStrategyRegistry(options) {
  const executeComponent = createComponentExecutionStrategy(
    createLocalComponentLoader(options));
  const executeRaw = createRawExecutionStrategy(createLocalRawLoader(options));
  return createArtifactStrategyRegistry([
    { kind: "raw", execute: executeRaw },
    { kind: "component", execute: executeComponent },
  ]);
}
