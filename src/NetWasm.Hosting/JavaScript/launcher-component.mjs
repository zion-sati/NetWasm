#!/usr/bin/env node

import { fileURLToPath } from "node:url";

import { createLocalComponentNetWasmExecution } from "./local-component-execution-root.mjs";
import { runLocalNetWasmLauncher } from "./local-launcher-bootstrap.mjs";

await runLocalNetWasmLauncher(
  createLocalComponentNetWasmExecution,
  fileURLToPath(import.meta.url));
