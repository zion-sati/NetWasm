const legacyDisposeSymbol = Symbol.for("dispose");
const maxUnsignedInt32 = 0xffffffff;

export function createPollableReactor({
  onReady,
  onFailure,
  schedule = globalThis.queueMicrotask?.bind(globalThis),
} = {}) {
  if (typeof onReady !== "function") {
    throw new TypeError("pollable readiness action is required");
  }
  if (typeof onFailure !== "function") {
    throw new TypeError("pollable failure action is required");
  }
  if (typeof schedule !== "function") {
    throw new TypeError("pollable scheduler is required");
  }

  const pending = new Map();
  let closed = false;

  const reportFailure = () => {
    try {
      onFailure();
    } catch {
      // Failure delivery is terminal notification plumbing. It must not create
      // an unhandled host exception after the original failure was contained.
    }
  };

  const release = entry => {
    entry.active = false;
    pending.delete(entry.token);
    try {
      const standardDispose = entry.pollable[Symbol.dispose];
      if (typeof standardDispose === "function") {
        standardDispose.call(entry.pollable);
      } else if (typeof entry.pollable[legacyDisposeSymbol] === "function") {
        entry.pollable[legacyDisposeSymbol]();
      } else if (typeof entry.pollable.dispose === "function") {
        entry.pollable.dispose();
      }
      return true;
    } catch {
      return false;
    }
  };

  const fail = entry => {
    release(entry);
    reportFailure();
  };

  const ready = entry => {
    if (!release(entry)) {
      reportFailure();
      return;
    }
    if (closed) return;
    try {
      onReady(entry.token);
    } catch {
      reportFailure();
    }
  };

  const watch = (pollable, token) => {
    if (closed) throw new Error("pollable reactor is closed");
    if (pollable === null || typeof pollable !== "object"
        || typeof pollable.block !== "function") {
      throw new TypeError("reactor watch requires a pollable");
    }
    if (!Number.isInteger(token) || token <= 0 || token > maxUnsignedInt32) {
      throw new TypeError("reactor token must be a nonzero unsigned 32-bit integer");
    }
    if (pending.has(token)) {
      throw new Error("reactor token is already watched");
    }

    const entry = { pollable, token, active: true };
    pending.set(token, entry);
    try {
      schedule(() => {
        if (!entry.active || closed) return;
        let blocked;
        try {
          blocked = pollable.block();
        } catch {
          fail(entry);
          return;
        }
        Promise.resolve(blocked).then(
          () => {
            if (entry.active && !closed) ready(entry);
          },
          () => {
            if (entry.active && !closed) fail(entry);
          });
      });
    } catch {
      fail(entry);
    }
  };

  const cancel = token => {
    if (!Number.isInteger(token) || token <= 0 || token > maxUnsignedInt32) {
      throw new TypeError("reactor token must be a nonzero unsigned 32-bit integer");
    }
    const entry = pending.get(token);
    if (entry === undefined) return false;
    if (!release(entry)) reportFailure();
    return true;
  };

  const close = () => {
    if (closed) return;
    closed = true;
    let releaseFailed = false;
    for (const entry of pending.values()) {
      if (!release(entry)) releaseFailed = true;
    }
    pending.clear();
    if (releaseFailed) {
      throw new Error("The execution host could not release every pending pollable.");
    }
  };

  return Object.freeze({ watch, cancel, close });
}
