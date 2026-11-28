// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

// Async imports taking JSObjects, and failed imports, run alike in the page and the worker: see shared/InteropCells.cs.
export async function checkInteropCells(target, check) {
  const ok = "1";
  const expected = {
    usingImport: ok, usingGlobalProperty: ok, neverDisposed: ok, disposeAfter: ok, tryFinally: ok, lambda: ok,
    registration: ok, registrationNoDispose: ok, immediateArray: "ok//0", delayedArray: "ok//20", nested: "ok//20",
    withToken: /^ok\/\/20\|err\/AbortError\//, concurrent: "ok//30|ok//10", subscriptionWhilePending: "ok//20",
  };
  // A handle StackTrace released is not released again by the exception's finalizer.
  document.body.dataset.testCase = "release-once";
  check(await target.objectCell("releaseOnceStart", false) === "started"
    && await target.objectCell("releaseOnceFinish", false) === "1",
    "A JSException's finalizer released a handle its StackTrace had already released.");
  for (const [cell, want] of Object.entries(expected)) {
    for (const displace of [false, true]) {
      document.body.dataset.testCase = `object-${cell}-${displace}`;
      const actual = await Promise.race([target.objectCell(cell, displace),
        new Promise(resolve => setTimeout(() => resolve("HANG"), 6000))]);
      check(typeof want === "string" ? actual === want : want.test(actual),
        `An async import taking a JSObject (${cell}, displaced ${displace}) gave ${actual}.`);
    }
  }
  const failures = {
    syncDom: "NotAllowedError: denied|same", syncType: "TypeError: bad argument|same",
    syncNamed: "QuotaExceededError: full|same", syncString: "plain failure|same", syncNumber: "42|same",
    direct: "RangeError: direct 1|same", rejectError: "NotFoundError: gone|same",
    rejectString: "plain failure|same", rejectNothing: "Error|other",
    fetchAbort: /^AbortError: .*\|same$/, fetchTimeout: "TimeoutError: slow|same",
  };
  for (const [cell, want] of Object.entries(failures)) {
    document.body.dataset.testCase = `failed-import-${cell}`;
    const actual = await target.failedImport(cell);
    check(typeof want === "string" ? actual === want : want.test(actual),
      `A failed import (${cell}) raised JSException "${actual}".`);
  }
  const extras = {
    managedSync: "same", managedAsync: "same", promiseImport: "NotFoundError: gone|same",
    stackTrace: "stack|ManagedError:TypeError: bad argument", equality: "self|unequal|none-equal",
  };
  for (const [cell, want] of Object.entries(extras)) {
    document.body.dataset.testCase = `failure-extra-${cell}`;
    const actual = await target.failureExtra(cell);
    check(actual === want, `${cell} gave "${actual}".`);
  }
}
