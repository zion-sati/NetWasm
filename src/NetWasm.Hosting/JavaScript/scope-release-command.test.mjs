import assert from "node:assert/strict";
import test from "node:test";
import { createScopeReleaseCommand } from "./scope-release-command.mjs";

test("closes an empty scope exactly once", async () => {
  const command = createScopeReleaseCommand();
  assert.equal(Object.isFrozen(command), true);
  const first = command.close();
  const second = command.close();
  assert.equal(first, second);
  const failures = await first;
  assert.deepEqual(failures, []);
  assert.equal(Object.isFrozen(failures), true);
});

test("validates every release before closing anything", () => {
  const calls = [];
  assert.throws(() => createScopeReleaseCommand(null), /array/);
  for (const invalid of [null, {}, 1, "release"]) {
    assert.throws(() => createScopeReleaseCommand([
      () => calls.push("first"),
      invalid,
    ]), /invalid/);
  }
  assert.deepEqual(calls, []);
});

test("attempts every release in reverse acquisition order and returns failures", async () => {
  const calls = [];
  const firstFailure = new Error("third failed");
  const secondFailure = new Error("second failed");
  const command = createScopeReleaseCommand([
    () => calls.push("first"),
    async () => {
      await Promise.resolve();
      calls.push("second");
      throw secondFailure;
    },
    () => {
      calls.push("third");
      throw firstFailure;
    },
    () => calls.push("fourth"),
  ]);

  const failures = await command.close();
  assert.deepEqual(calls, ["fourth", "third", "second", "first"]);
  assert.deepEqual(failures, [firstFailure, secondFailure]);
  assert.equal(Object.isFrozen(failures), true);
  assert.equal(await command.close(), failures);
  assert.deepEqual(calls, ["fourth", "third", "second", "first"]);
});

test("installs the shared completion before a release can reenter close", async () => {
  let nestedClose;
  let releases = 0;
  let command;
  command = createScopeReleaseCommand([() => {
    releases++;
    nestedClose = command.close();
  }]);

  const firstClose = command.close();
  assert.equal(nestedClose, firstClose);
  assert.deepEqual(await firstClose, []);
  assert.equal(releases, 1);
  assert.equal(command.close(), firstClose);
});
