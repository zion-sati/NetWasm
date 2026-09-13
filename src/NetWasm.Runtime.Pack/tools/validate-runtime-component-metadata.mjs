import fs from 'node:fs';
import { validateRuntimeComponentMetadata } from './runtime-component-metadata-validator.mjs';

const module = new WebAssembly.Module(fs.readFileSync(process.argv[2]));
validateRuntimeComponentMetadata(
  WebAssembly.Module.customSections(module, 'component-type:netwasm-runtime'),
  fs.readFileSync(process.argv[3]));
