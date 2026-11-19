// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: MIT

import { createIdleCollection } from "./idle-collection.mjs";
import { createQuietCollection } from "./quiet-collection.mjs";

// What a session does for its module's collector each time the module ran: it lets the collector use the
// host's next idle period, and the quiet that follows the module's last run, where the runtime has the entry
// (collectWhenIdle, collectWhenQuiet) and the host the means (requestIdle, quietTimer; null without). The
// action returned is called once for each run of the module, and does nothing where neither applies.
export function createSessionCollection(options) {
  if (options === null || typeof options !== "object") {
    throw new TypeError("session collection options are invalid");
  }
  const { collectWhenIdle, collectWhenQuiet, requestIdle, quietTimer } = options;
  const quiet = quietTimer !== null && typeof collectWhenQuiet === "function"
    ? createQuietCollection({ collect: collectWhenQuiet, now: quietTimer.now, setTimer: quietTimer.setTimer })
    : null;
  // An idle collection that ran leaves the quiet one nothing to do until the module runs again.
  const idle = requestIdle !== null && typeof collectWhenIdle === "function"
    ? createIdleCollection({ collect: () => { if (collectWhenIdle()) quiet?.collected(); }, requestIdle })
    : null;
  return () => {
    idle?.request();
    quiet?.moduleRan();
  };
}
