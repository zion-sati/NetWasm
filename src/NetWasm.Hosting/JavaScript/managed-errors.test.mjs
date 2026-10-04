import assert from "node:assert/strict";
import test from "node:test";
import {
  managedExceptionBrand,
  NetWasmHostError,
  NetWasmManagedError,
} from "./managed-errors.mjs";

test("host errors preserve their stable name and optional cause", () => {
  const cause = new Error("host cause");
  const error = new NetWasmHostError("host failed", { cause });

  assert.equal(error.name, "NetWasmHostError");
  assert.equal(error.message, "host failed");
  assert.equal(error.cause, cause);
  assert.ok(error instanceof Error);
});

test("managed errors preserve export identity, type, cause, and global brand", () => {
  const cause = new Error("managed cause");
  const error = new NetWasmManagedError("run", { cause, managedType: 7 });

  assert.equal(error.name, "NetWasmManagedError");
  assert.equal(error.message, "managed export run failed");
  assert.equal(error.exportName, "run");
  assert.equal(error.managedType, 7);
  assert.equal(error.cause, cause);
  assert.equal(error[managedExceptionBrand], true);
  assert.equal(managedExceptionBrand, Symbol.for("NetWasm.ManagedException"));
});

test("managed and host errors allow absent optional details", () => {
  const host = new NetWasmHostError("host failed");
  const managed = new NetWasmManagedError("run");

  assert.equal(host.cause, undefined);
  assert.equal(managed.cause, undefined);
  assert.equal(managed.managedType, undefined);
});

test("managed errors expose copied payloads and distinguish empty from absent messages", () => {
  for (const message of ["original 🌏", "", null]) {
    for (const typeName of ["System.FormatException", null]) {
      const managed = { typeId: 7, typeName, message, stackTrace: "managed trace" };
      const error = new NetWasmManagedError("run", { managed });
      assert.equal(error.message, message ?? typeName ?? "managed export run failed");
      assert.equal(error.managedType, 7);
      assert.deepEqual(error.managed, managed);
      assert.notEqual(error.managed, managed);
      assert.equal(Object.isFrozen(error.managed), true);
    }
  }
  assert.throws(() => new NetWasmManagedError("run", { managed: {} }), TypeError);
});
