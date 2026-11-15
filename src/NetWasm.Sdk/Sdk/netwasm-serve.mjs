#!/usr/bin/env node
// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

// The command dotnet run starts for a browser project; see netwasm-dev-server.mjs.
import { run } from "./netwasm-dev-server.mjs";

const waits = await run(process.argv.slice(2), message => process.stdout.write(message + "\n")).catch(error => {
  process.stderr.write(`netwasm-serve: ${error.message}\n`);
  process.exitCode = 1;
  return false;
});
// The launcher waits until dotnet run, or dotnet watch, stops it; the session's server stays.
if (waits) setInterval(() => {}, 1 << 30);
