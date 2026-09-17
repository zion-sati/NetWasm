import fs from 'node:fs';
import { validateRuntimeComponentMetadata } from './runtime-component-metadata-validator.mjs';
import { readCustomSections } from './wasm-section-reader.mjs';

validateRuntimeComponentMetadata(
  readCustomSections(fs.readFileSync(process.argv[2]), 'component-type:netwasm-runtime'),
  fs.readFileSync(process.argv[3]));
