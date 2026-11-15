#!/usr/bin/env node
// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: MIT

import { fileURLToPath } from "node:url";

import { createLocalComponentNetWasmExecution } from "./local-component-execution-root.mjs";
import { runLocalNetWasmLauncher } from "./local-launcher-bootstrap.mjs";

await runLocalNetWasmLauncher(
  createLocalComponentNetWasmExecution,
  fileURLToPath(import.meta.url));
