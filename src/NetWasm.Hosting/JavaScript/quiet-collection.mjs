// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: MIT

// A program that drops what it held without allocating, as when it clears a table of rows, leaves garbage
// that no growth of the heap announces. The collector's idle entry collects only once the heap has grown, so
// that garbage would wait for the program's next burst of allocation and be collected in the middle of it.
// A host with a timer gives the collector the time after the module goes quiet instead: once the module has
// run and then has not run for a whole period, the host calls the runtime's quiet entry, which collects
// whatever the growth. One collection follows each stretch of activity, and none follows a stretch an idle
// collection already cleaned up after.
//
// A run of the module costs two flags here: no timer is set or reset for it. The timer runs out the period
// and, when the module ran meanwhile, runs out another.
//
// The quiet entry answers with the share of the heap it freed. A collection that freed little only traced a
// heap that is mostly alive, so the next period is twice as long, up to a limit; one that paid starts over at
// the shortest. A period is also never shorter than a multiple of what the last collection took, so a large
// heap's collection stays a small share of the time the host was quiet anyway.
const shortestPeriodMilliseconds = 1000;
const longestPeriodMilliseconds = 60000;
// A collection takes at most one part in this many of the quiet time before the next.
const restPerCollection = 20;
// A collection paid when it freed an eighth of what the heap occupied; the runtime answers in 255ths.
const paidShare = 32;

export function createQuietCollection(options) {
  if (options === null || typeof options !== "object" || typeof options.collect !== "function"
      || typeof options.setTimer !== "function" || typeof options.now !== "function") {
    throw new TypeError("quiet collection requires a collect action, a timer and a clock");
  }
  const { collect, setTimer, now } = options;
  let ranSinceTimer = false;
  let ranSinceCollection = false;
  let waiting = false;
  let period = shortestPeriodMilliseconds;
  const expire = () => {
    if (ranSinceTimer) {
      ranSinceTimer = false;
      setTimer(expire, period);
      return;
    }
    waiting = false;
    if (!ranSinceCollection) return;
    ranSinceCollection = false;
    const started = now();
    // Zero when the runtime did not collect; otherwise one more than the share it freed.
    const answer = collect();
    if (answer === 0) return;
    const rest = (now() - started) * restPerCollection;
    period = Math.min(longestPeriodMilliseconds,
      Math.max(answer - 1 >= paidShare ? shortestPeriodMilliseconds : period * 2, rest));
  };
  return Object.freeze({
    // The module ran.
    moduleRan() {
      ranSinceCollection = true;
      if (waiting) {
        ranSinceTimer = true;
        return;
      }
      waiting = true;
      setTimer(expire, period);
    },
    // Another collection has run since the module did.
    collected() {
      ranSinceCollection = false;
    },
  });
}
