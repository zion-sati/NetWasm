import assert from "node:assert/strict";
import test from "node:test";
import { createReactorOracleImports } from "./reactor-oracle-imports.mjs";

for (const [target, canonicalPrefix] of [
  ["wasm32", "cm32p2"],
  ["wasm64", "cm64p2"],
]) {
  test(`creates the complete ${target} reactor oracle import contract`, () => {
    const imports = createReactorOracleImports(target);

    assert.deepEqual(Object.keys(imports).sort(), [
      `${canonicalPrefix}|netwasm:runtime/reactor-host@1`,
      `${canonicalPrefix}|wasi:clocks/monotonic-clock@0.2`,
    ]);
    assert.equal(
      imports[`${canonicalPrefix}|netwasm:runtime/reactor-host@1`].watch(7, 11),
      undefined,
    );
    assert.equal(
      imports[`${canonicalPrefix}|netwasm:runtime/reactor-host@1`].cancel(7),
      undefined,
    );
    assert.equal(
      typeof imports[`${canonicalPrefix}|wasi:clocks/monotonic-clock@0.2`].now(),
      "bigint",
    );
    assert.equal(
      imports[`${canonicalPrefix}|wasi:clocks/monotonic-clock@0.2`]["subscribe-duration"](0n),
      1,
    );
  });

  test(`forwards ${target} reactor scheduling to supplied hooks`, () => {
    const events = [];
    const imports = createReactorOracleImports(target, {
      watch: (pollable, token) => events.push(["watch", pollable, token]),
      cancel: token => events.push(["cancel", token]),
    });
    const reactor = imports[
      `${canonicalPrefix}|netwasm:runtime/reactor-host@1`];

    reactor.watch(7, 11);
    reactor.cancel(11);

    assert.deepEqual(events, [["watch", 7, 11], ["cancel", 11]]);
  });
}
