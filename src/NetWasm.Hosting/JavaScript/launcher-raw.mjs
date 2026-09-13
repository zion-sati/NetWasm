#!/usr/bin/env node

import { fileURLToPath } from "node:url";

import { createLocalRawNetWasmExecution } from "./local-raw-execution-root.mjs";
import { runLocalNetWasmLauncher } from "./local-launcher-bootstrap.mjs";

await runLocalNetWasmLauncher(
  createLocalRawNetWasmExecution,
  fileURLToPath(import.meta.url));
