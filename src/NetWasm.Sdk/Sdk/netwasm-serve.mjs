#!/usr/bin/env node

// The command dotnet run starts for a browser project; see netwasm-dev-server.mjs.
import { run } from "./netwasm-dev-server.mjs";

const waits = await run(process.argv.slice(2), message => process.stdout.write(message + "\n")).catch(error => {
  process.stderr.write(`netwasm-serve: ${error.message}\n`);
  process.exitCode = 1;
  return false;
});
// The launcher waits until dotnet run, or dotnet watch, stops it; the session's server stays.
if (waits) setInterval(() => {}, 1 << 30);
