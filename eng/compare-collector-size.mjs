// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: MIT

import { readFile, writeFile } from 'node:fs/promises';
import { createHash } from 'node:crypto';
import { brotliCompressSync, gzipSync, constants } from 'node:zlib';

const [baseline, candidate, output, scope] = process.argv.slice(2);
if (!baseline || !candidate || !output) throw new Error('Expected baseline, candidate and receipt paths.');
const results = {};
results.nodeVersion = process.version;
for (const [name, path] of [['compact', baseline], ['netwasm', candidate]]) {
  const bytes = await readFile(path);
  results[name] = {
    rawBytes: bytes.length,
    brotli11Bytes: brotliCompressSync(bytes, {
      params: { [constants.BROTLI_PARAM_QUALITY]: 11 },
    }).length,
    gzip9Bytes: gzipSync(bytes, { level: 9 }).length,
    sha256: createHash('sha256').update(bytes).digest('hex'),
  };
}
results.scope = scope ?? 'Complete optimized Preview 2 components; identical application and command WIT, collector-selected runtime.';
results.delta = {};
for (const field of ['rawBytes', 'brotli11Bytes', 'gzip9Bytes']) {
  results.delta[field] = results.netwasm[field] - results.compact[field];
}
await writeFile(output, JSON.stringify(results, null, 2) + '\n');
console.log(JSON.stringify({ compact: results.compact.rawBytes, netwasm: results.netwasm.rawBytes, delta: results.delta }));
