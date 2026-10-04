import assert from "node:assert/strict";
import test from "node:test";
import { bindWorkerImports } from "./worker-import-bindings.mjs";

test("static providers remain unchanged", () => {
  const namespace = { ordinary: value => value, constant: 42 };
  assert.equal(bindWorkerImports(namespace, undefined), namespace);
});

test("each provider instance captures its own notifier and application signatures", () => {
  let created = 0;
  const namespace = { createWorkerImports(notify) {
    created++;
    return {
      customProgress(completed, total) { notify("progress", [completed, total]); },
      customMessage(message, details) { notify("message", [message, details]); },
    };
  } };
  const first = [];
  const second = [];
  const a = bindWorkerImports(namespace, (name, args) => first.push({ name, args }));
  const b = bindWorkerImports(namespace, (name, args) => second.push({ name, args }));
  a.customProgress(42, 100);
  b.customMessage("hello", { count: 3 });
  assert.deepEqual(first, [{ name: "progress", args: [42, 100] }]);
  assert.deepEqual(second, [{ name: "message", args: ["hello", { count: 3 }] }]);
  assert.equal(created, 2);
  assert.equal(Object.isFrozen(a), true);
});

test("invalid factories fail startup instead of creating partial imports", () => {
  const notify = () => {};
  assert.throws(() => bindWorkerImports({ createWorkerImports: 42 }, notify), TypeError);
  assert.throws(() => bindWorkerImports({ createWorkerImports() {} }, undefined), TypeError);
  const accessor = {}; Object.defineProperty(accessor, "x", { enumerable: true, get() { throw new Error("must not read accessor"); } });
  const symbol = { [Symbol("x")]: () => {} };
  const hidden = {}; Object.defineProperty(hidden, "x", { value() {} });
  for (const result of [null, [], 42, Promise.resolve({}), new Date(), { x: 42 }, accessor, symbol, hidden,
    { createWorkerImports() {} }]) {
    assert.throws(() => bindWorkerImports({ createWorkerImports: () => result }, notify), TypeError);
  }
  const plain = Object.create(null); plain.report = () => 42;
  assert.equal(bindWorkerImports({ createWorkerImports: () => plain }, notify).report(), 42);
});
