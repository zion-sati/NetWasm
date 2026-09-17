import fs from 'node:fs';
import { validateRuntimeImports } from './runtime-import-validator.mjs';
import { readFunctionImports } from './wasm-section-reader.mjs';

validateRuntimeImports(readFunctionImports(fs.readFileSync(process.argv[2])), process.argv[3]);
