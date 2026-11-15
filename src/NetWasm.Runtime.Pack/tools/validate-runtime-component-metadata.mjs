// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: MIT

import fs from 'node:fs';
import { validateRuntimeComponentMetadata } from './runtime-component-metadata-validator.mjs';
import { readCustomSections } from './wasm-section-reader.mjs';

validateRuntimeComponentMetadata(
  readCustomSections(fs.readFileSync(process.argv[2]), 'component-type:netwasm-runtime'),
  fs.readFileSync(process.argv[3]));
