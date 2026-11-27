// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: MIT

import assert from "node:assert/strict";
import test from "node:test";

import { createJavaScriptErrorChannel } from "./javascript-error-channel.mjs";

function recordingHandles() {
  const values = new Map();
  let next = 1;
  return {
    values,
    acquire(value) {
      const handle = next++;
      values.set(handle, value);
      return handle;
    },
    get: handle => values.get(handle),
  };
}

test("an Error's message is Error.prototype.toString's, and the error itself is kept", () => {
  const handles = recordingHandles();
  const channel = createJavaScriptErrorChannel(handles);
  const error = new DOMException("Permission denied", "NotAllowedError");

  channel.captureThrow(error);

  assert.equal(channel.services.error_pending_message(), "NotAllowedError: Permission denied");
  const handle = channel.services.error_take();
  assert.equal(handles.get(handle), error);
  assert.equal(channel.services.error_pending_message(), null);
  assert.equal(channel.services.error_take(), 0);
});

test("a TypeError and an Error with its own name read as name and message", () => {
  const channel = createJavaScriptErrorChannel(recordingHandles());
  channel.captureThrow(new TypeError("x is not a function"));
  assert.equal(channel.services.error_pending_message(), "TypeError: x is not a function");
  channel.services.error_take();
  const custom = new Error("quota");
  custom.name = "QuotaExceededError";
  channel.captureRejection(custom);
  assert.equal(channel.services.error_pending_message(), "QuotaExceededError: quota");
});

test("a thrown string is its own message and the string is kept", () => {
  const handles = recordingHandles();
  const channel = createJavaScriptErrorChannel(handles);

  channel.captureThrow("plain failure");

  assert.equal(channel.services.error_pending_message(), "plain failure");
  assert.equal(handles.get(channel.services.error_take()), "plain failure");
});

test("a rejection without a reason, or with a falsy one, becomes a new Error as on .NET", () => {
  for (const reason of [undefined, null, 0, "", false]) {
    const handles = recordingHandles();
    const channel = createJavaScriptErrorChannel(handles);
    channel.captureRejection(reason);
    assert.equal(channel.services.error_pending_message(), "Error");
    assert.ok(handles.get(channel.services.error_take()) instanceof Error);
  }
});

test("a thrown null or undefined raises an Error, where .NET raises nothing", () => {
  for (const value of [undefined, null]) {
    const channel = createJavaScriptErrorChannel(recordingHandles());
    channel.captureThrow(value);
    assert.equal(channel.services.error_pending_message(), "Error");
  }
});

test("a thrown number, unlike a falsy rejection, keeps its value", () => {
  const handles = recordingHandles();
  const channel = createJavaScriptErrorChannel(handles);
  channel.captureThrow(0);
  assert.equal(channel.services.error_pending_message(), "0");
  assert.equal(handles.get(channel.services.error_take()), 0);
});

test("a value whose toString throws fails with that error", () => {
  const handles = recordingHandles();
  const channel = createJavaScriptErrorChannel(handles);
  channel.captureThrow(Object.create(null));
  assert.match(channel.services.error_pending_message(), /^TypeError: /);
  assert.ok(handles.get(channel.services.error_take()) instanceof TypeError);
});

test("the latest failure replaces one nothing took", () => {
  const channel = createJavaScriptErrorChannel(recordingHandles());
  channel.captureThrow(new Error("first"));
  channel.captureThrow(new Error("second"));
  assert.equal(channel.services.error_pending_message(), "Error: second");
});

test("an error's stack is read from its stack property; other values have none", () => {
  const handles = recordingHandles();
  const channel = createJavaScriptErrorChannel(handles);
  const error = new Error("boom");
  const withStack = handles.acquire(error);
  const plain = handles.acquire("text");
  const object = handles.acquire({ stack: 7 });
  assert.equal(channel.services.error_stack(withStack), error.stack);
  assert.equal(channel.services.error_stack(plain), null);
  assert.equal(channel.services.error_stack(object), null);
});

test("compares two handles by the value they hold", () => {
  const handles = recordingHandles();
  const channel = createJavaScriptErrorChannel(handles);
  const error = new Error("one");
  const first = handles.acquire(error);
  const second = handles.acquire(error);
  const other = handles.acquire(new Error("one"));
  assert.equal(channel.services.error_same(first, second), true);
  assert.equal(channel.services.error_same(first, other), false);
});

test("an export's offered error is taken once, and its handle released", () => {
  const released = [];
  const handles = { ...recordingHandles(), release: handle => released.push(handle) };
  const channel = createJavaScriptErrorChannel(handles);
  const error = new DOMException("gone", "NotFoundError");
  const handle = handles.acquire(error);
  channel.services.error_offer_export(handle);
  assert.deepEqual(released, [handle]);
  assert.equal(channel.takeExportFailure().value, error);
  assert.equal(channel.takeExportFailure(), null);
});

test("exposes the pending value for a managed exception's lookup", () => {
  const channel = createJavaScriptErrorChannel(recordingHandles());
  assert.equal(channel.pendingValue(), undefined);
  const error = new Error("x");
  channel.captureThrow(error);
  assert.equal(channel.pendingValue(), error);
});

test("drops an error nothing took", () => {
  const channel = createJavaScriptErrorChannel(recordingHandles());
  channel.captureRejection(new Error("unused"));
  channel.discardPending();
  assert.equal(channel.services.error_pending_message(), null);
  assert.equal(channel.services.error_take(), 0);
});

test("says whether an export's offered error is waiting", () => {
  const handles = recordingHandles();
  const channel = createJavaScriptErrorChannel({ ...handles, release() {} });
  assert.equal(channel.hasExportFailure(), false);
  channel.services.error_offer_export(handles.acquire(new Error("x")));
  assert.equal(channel.hasExportFailure(), true);
  channel.takeExportFailure();
  assert.equal(channel.hasExportFailure(), false);
});
