#!/usr/bin/env node
// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: MIT

import { fileURLToPath } from "node:url";

import { createLocalRawNetWasmExecution } from "./local-raw-execution-root.mjs";
import { runLocalNetWasmLauncher } from "./local-launcher-bootstrap.mjs";

await runLocalNetWasmLauncher(
  createLocalRawNetWasmExecution,
  fileURLToPath(import.meta.url));
