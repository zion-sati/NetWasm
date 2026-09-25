import { resolve } from 'node:path';

export function expected(input, edited, referenceEdited) {
  let result = edited ? 19 : 17;
  for (let index = 0; index < 7; index++)
    result = (Math.imul(result, 3) + ((index & 1) === 0 ? (input + index) | 0 : (input - index) | 0) + (referenceEdited ? 5 : 0)) | 0;
  return result;
}

export function cacheMatches(phase, metrics) {
  if (!metrics) return false;
  if (['disabled', 'cold', 'reference-edited'].includes(phase))
    return metrics.Hits === 0 && metrics.Misses > 0;
  if (phase === 'memory-warm') return metrics.Hits > 0 && metrics.MemoryHits > 0;
  if (['disk-warm', 'edited', 'reverted', 'reference-reverted'].includes(phase))
    return metrics.Hits > 0 && metrics.DiskHits > 0;
  throw Error('unknown-cache-phase');
}

export function resolveCompilerPath(sdkList, pinnedVersion) {
  const matches = sdkList.split(/\r?\n/).map(line => line.match(/^(\S+) \[(.+)\]$/))
    .filter(match => match && match[1] === pinnedVersion);
  if (matches.length !== 1) throw Error('pinned-sdk-not-unique');
  return resolve(matches[0][2], pinnedVersion, 'Roslyn/bincore/csc.dll');
}
