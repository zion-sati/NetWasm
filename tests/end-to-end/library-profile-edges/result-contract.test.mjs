import assert from 'node:assert/strict';
import { mkdtempSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { spawnSync } from 'node:child_process';
import { fileURLToPath } from 'node:url';
import test from 'node:test';

const script = fileURLToPath(new URL('./run.sh', import.meta.url));
const complete = Array.from({ length: 15 }, (_, input) => `CASE ${input} PASS`);
const examples = [
  ['complete ordered results', complete, true],
  ['start markers and unrelated build output', ['build', ...complete.flatMap((line, input) => [`CASE ${input} START`, line])], true],
  ['empty output despite successful launcher', [], false],
  ['pending first case', ['CASE 0 START'], false],
  ['missing last case', complete.slice(0, -1), false],
  ['duplicate case', [...complete, 'CASE 14 PASS'], false],
  ['reordered cases', [complete[1], complete[0], ...complete.slice(2)], false],
  ['one failed case', complete.map((line, input) => input === 7 ? 'CASE 7 FAIL' : line), false],
  ['failure plus full pass set', [...complete, 'CASE 7 FAIL'], false],
  ['out-of-range case', [...complete, 'CASE 15 PASS'], false],
];

for (const [name, lines, accepted] of examples) {
  test(name, () => {
    const directory = mkdtempSync(join(tmpdir(), 'netwasm-result-contract-'));
    try {
      const output = join(directory, 'stdout');
      writeFileSync(output, lines.join('\n') + '\n');
      const result = spawnSync('bash', [script, '--verify-results', output], { encoding: 'utf8' });
      assert.equal(result.error, undefined);
      assert.equal(result.signal, null);
      assert.equal(result.status === 0, accepted);
    } finally {
      rmSync(directory, { recursive: true });
    }
  });
}
