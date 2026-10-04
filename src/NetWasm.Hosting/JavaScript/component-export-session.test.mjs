import assert from "node:assert/strict";
import test from "node:test";
import { openComponentExportSession } from "./component-export-session.mjs";

test("composes one persistent scalar component session", async () => {
  let instantiations = 0;
  let value = 0;
  const session = await openComponentExportSession({
    adapter: {
      contractKey: "netwasm:worker/wit@1.0.0",
      async instantiate({ reactorHost }) {
        instantiations++;
        reactorHost.assertAvailable();
        return {
          exports: {
            add(delta) { value += delta; return value; },
            read() { return value; },
          },
          guestWake() {},
        };
      },
    },
    imports: Object.freeze({}),
    loadCoreModule() {},
  });

  assert.equal(session.exports.add(20), 20);
  assert.equal(session.exports.add(22), 42);
  assert.equal(session.exports.read(), 42);
  assert.equal(instantiations, 1);
  await session.close();
  assert.equal(await session.failure, null);
});
