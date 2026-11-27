// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

// The JavaScript side of InteropCells.cs, in the page or the worker.
globalThis.cellsController ??= new AbortController();

const result = (ok, value) => ok
  ? ["ok", "", String(value)]
  : ["err", String(value?.name ?? typeof value), String(value?.message ?? value)];

export function createController() { return new AbortController(); }
export function abort(controller) { controller.abort(); }
export function asyncObjToInt(value) { return Promise.resolve(value instanceof AbortController ? 1 : 0); }
export function delay(milliseconds, controller) {
  return new Promise(resolve => {
    if (controller.signal.aborted) return resolve(result(false, controller.signal.reason));
    const timer = setTimeout(() => resolve(result(true, milliseconds)), milliseconds);
    controller.signal.addEventListener("abort", () => {
      clearTimeout(timer);
      resolve(result(false, controller.signal.reason));
    }, { once: true });
  });
}
export function immediate(controller) { return Promise.resolve(result(true, controller.signal.aborted ? "aborted" : 0)); }
export function listen(callback, target) {
  target.signal.addEventListener("abort", callback);
  return { dispose() { target.signal.removeEventListener("abort", callback); } };
}

// The last value an import below threw or rejected with, which the exception gives back.
let lastFailure;
function failure(kind) {
  switch (kind) {
    case "syncDom": return new DOMException("denied", "NotAllowedError");
    case "syncType": return new TypeError("bad argument");
    case "syncNamed": { const error = new Error("full"); error.name = "QuotaExceededError"; return error; }
    case "syncString": case "rejectString": return "plain failure";
    case "syncNumber": return 42;
    case "rejectError": return new DOMException("gone", "NotFoundError");
    case "rejectNothing": return undefined;
    default: throw new Error("unknown failure " + kind);
  }
}
export function throwValue(kind) {
  lastFailure = failure(kind);
  globalThis.cellsLastFailure = lastFailure;
  throw lastFailure;
}
export function throwDirect(kind) {
  lastFailure = new RangeError("direct " + kind);
  throw lastFailure;
}
export function rejectWith(kind) {
  lastFailure = failure(kind);
  globalThis.cellsLastFailure = lastFailure;
  return Promise.reject(lastFailure);
}
export function rejectLater(kind) {
  lastFailure = failure(kind);
  return Promise.reject(lastFailure);
}
export function throwManaged(error) { throw error; }
export function rejectManaged(error) { return Promise.reject(error); }
export function touch(value) { return value instanceof AbortController ? 1 : 0; }
export function lastStackTail() {
  const stack = typeof lastFailure?.stack === "string" ? lastFailure.stack : "";
  const top = String(lastFailure) + "\n";
  return stack.startsWith(top) ? stack.slice(top.length) : stack;
}
export async function abortedFetch(url, reason) {
  const controller = new AbortController();
  controller.abort(reason === "" ? undefined : new DOMException("slow", reason));
  try {
    await fetch(url, { signal: controller.signal });
  } catch (error) {
    lastFailure = error;
    throw error;
  }
  return 0;
}

let kept;
export function keep(error) { kept = error; }
export function keptOriginal() { return kept === lastFailure; }
export function keptDescription() { return `${kept?.name}:${kept?.message}`; }
