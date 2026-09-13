#!/usr/bin/env node

import { fileURLToPath } from "node:url";

import { createLocalNetWasmExecution } from "./local-execution-root.mjs";
import { runLocalNetWasmLauncher } from "./local-launcher-bootstrap.mjs";

await runLocalNetWasmLauncher(
  createLocalNetWasmExecution,
  fileURLToPath(import.meta.url));
