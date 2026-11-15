// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: MIT

import { createBrowserBootstrapComposition } from "./browser-bootstrap-composition.mjs";
import { createBrowserRawNetWasmExecution } from "./browser-raw-execution-root.mjs";

export function createBrowserNetWasmBootstrap(options, consumerModules = {}) {
  return createBrowserBootstrapComposition(
    options,
    value => createBrowserRawNetWasmExecution(value, consumerModules));
}
