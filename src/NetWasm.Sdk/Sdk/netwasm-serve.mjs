#!/usr/bin/env node

// The command dotnet run starts for a browser project; see netwasm-dev-server.mjs.
import { serve } from "./netwasm-dev-server.mjs";

await serve(process.argv.slice(2), message => process.stdout.write(message + "\n")).catch(error => {
  process.stderr.write(`netwasm-serve: ${error.message}\n`);
  process.exitCode = 1;
});
