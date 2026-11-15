// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: MIT

import { createBrowserBootstrapComposition } from "./browser-bootstrap-composition.mjs";
import { createBrowserComponentNetWasmExecution } from "./browser-component-execution-root.mjs";

export function createBrowserNetWasmBootstrap(options) {
  return createBrowserBootstrapComposition(
    options,
    createBrowserComponentNetWasmExecution);
}
