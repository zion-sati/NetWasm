// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: MIT

// A host without requestIdleCallback is idle when its event loop reaches a timer set this long after the
// module last ran: a page in Safari, a worker, Node.js.
const fallbackDelayMilliseconds = 200;

// The scheduler that calls back when the host is idle: the browser's own in a page that has one, and
// otherwise a short timer, which runs once the event loop has nothing queued ahead of it. A timer that would
// keep a process alive (Node.js) is released from doing so. Null for a host with neither: there the module's
// collector paces itself by allocation alone.
export function selectIdleScheduler(scope) {
  if (scope === null || typeof scope !== "object") {
    throw new TypeError("idle scheduler scope is invalid");
  }
  if (typeof scope.requestIdleCallback === "function") {
    return callback => { scope.requestIdleCallback(() => callback()); };
  }
  if (typeof scope.setTimeout === "function") {
    return callback => { released(scope.setTimeout(() => callback(), fallbackDelayMilliseconds)); };
  }
  return null;
}

// The timer and clock a host's quiet collection runs on (quiet-collection.mjs), or null for a host without
// them. The runtime has no clock of its own: the host times the wait and the collection.
export function selectQuietTimer(scope) {
  if (scope === null || typeof scope !== "object") {
    throw new TypeError("quiet timer scope is invalid");
  }
  if (typeof scope.setTimeout !== "function" || scope.performance === null
      || typeof scope.performance !== "object" || typeof scope.performance.now !== "function") {
    return null;
  }
  return Object.freeze({
    now: () => scope.performance.now(),
    setTimer: (callback, milliseconds) => { released(scope.setTimeout(() => callback(), milliseconds)); },
  });
}

// Node.js keeps a process alive for a pending timer unless the timer is unreferenced; a browser's timer is a
// number.
function released(timer) {
  if (timer !== null && typeof timer === "object" && typeof timer.unref === "function") timer.unref();
}
