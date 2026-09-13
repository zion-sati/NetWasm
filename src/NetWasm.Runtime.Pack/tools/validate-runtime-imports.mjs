import fs from 'node:fs';
import { validateRuntimeImports } from './runtime-import-validator.mjs';

const module = new WebAssembly.Module(fs.readFileSync(process.argv[2]));
validateRuntimeImports(WebAssembly.Module.imports(module), process.argv[3]);
