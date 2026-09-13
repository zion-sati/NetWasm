import assert from "node:assert/strict";
import test from "node:test";

import { createManagedExceptionOutput } from "./managed-exception-output.mjs";

test("writes stable immediate and enriched diagnostics to explicit stderr", () => {
  const lines = [];
  const output = createManagedExceptionOutput(Object.freeze({
    write: bytes => lines.push(new TextDecoder().decode(bytes)),
  }));
  assert.equal(Object.isFrozen(output), true);
  output.reportImmediate({ typeId: 7, message: "boom", private: "ignored" });
  output.reportImmediate({ typeId: 8, message: null });
  output.reportEnriched({ typeId: 7, typeName: "Example.Boom", private: "ignored" });
  assert.deepEqual(lines, [
    "Managed exception #7: boom\n",
    "Managed exception #8: <no stored message>\n",
    "Managed exception #7 type: Example.Boom\n",
  ]);
});

test("requires an immutable synchronous stderr sink", () => {
  for (const value of [null, {}, { write() {} }, Object.freeze({ write: null })]) {
    assert.throws(() => createManagedExceptionOutput(value), /output sink/);
  }
});
