import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import test from 'node:test';
import { fileURLToPath } from 'node:url';

const builder = fileURLToPath(new URL('../../eng/build-netwasm-runtime.sh', import.meta.url));

// Builder-boundary tests: rejection must precede Node/toolchain setup and output.
for (const target of ['wasm32', 'wasm64']) {
  for (const scenario of ['missing-source', 'archive-source']) {
    test(`${target}: rejects ${scenario} before build setup`, () => {
      const directory = fs.mkdtempSync(path.join(os.tmpdir(), 'netwasm-builder-contract-'));
      try {
        const layout = path.join(directory, 'layout.json');
        const source = path.join(directory, 'probe.c');
        const output = path.join(directory, 'runtime.wasm');
        const invoked = path.join(directory, 'node-invoked');
        fs.writeFileSync(layout, JSON.stringify({
          schemaVersion: 2, target, applicationStaticDataEnd: 65536,
        }));
        fs.writeFileSync(path.join(directory, 'node'),
          '#!/bin/sh\n: > "$NETWASM_BUILDER_TEST_MARKER"\nexit 99\n', { mode: 0o755 });
        const args = [builder, '--runtime-layout', layout, '--target', target,
          '--output', output, '--additional-source', source];
        if (scenario === 'archive-source') {
          fs.writeFileSync(source, 'int probe(void) { return 1; }\n');
          args.push('--relocatable');
        }
        const result = spawnSync('bash', args, {
          encoding: 'utf8',
          env: { ...process.env, PATH: `${directory}${path.delimiter}${process.env.PATH}`,
            NETWASM_BUILDER_TEST_MARKER: invoked },
        });
        assert.equal(result.error, undefined);
        assert.equal(result.signal, null);
        assert.equal(result.status, 2);
        assert.equal(result.stdout, '');
        assert.equal(result.stderr.trim(), scenario === 'missing-source'
          ? 'missing additional runtime source'
          : 'additional sources require a final runtime module');
        assert.equal(fs.existsSync(output), false);
        assert.equal(fs.existsSync(invoked), false);
      } finally {
        fs.rmSync(directory, { recursive: true, force: true });
      }
    });
  }
}
