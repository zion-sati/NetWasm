import assert from "node:assert/strict";
import test from "node:test";
import {
  bindInteropExecutionInstance,
  closeInteropExecution,
  prepareInteropExecution,
} from "./interop-execution-preparation.mjs";

function create(overrides = {}) {
  const observations = [];
  let readers;
  const imports = { platform: { value() {} } };
  const memory = {};
  const boundary = {};
  const preparation = prepareInteropExecution({
    createImports(value) {
      readers = value;
      observations.push(["prepare", readers.readInstance(), readers.readMemory()]);
      return imports;
    },
    resolveMemory(instance) {
      observations.push(["resolve", readers.readInstance(), readers.readMemory()]);
      assert.equal(readers.readInstance(), instance);
      return memory;
    },
    bindInstance() {
      observations.push(["bind", readers.readInstance(), readers.readMemory()]);
      return boundary;
    },
    close() { observations.push(["close"]); },
    ...overrides,
  });
  return { boundary, imports, memory, observations, preparation, readers };
}

test("prepares immutable imports and binds one deferred instance", () => {
  const fixture = create();
  const instance = {};

  assert.equal(Object.isFrozen(fixture.preparation), true);
  assert.equal(fixture.preparation.imports, fixture.imports);
  assert.equal(bindInteropExecutionInstance({
    preparation: fixture.preparation,
    instance,
  }), fixture.boundary);
  assert.deepEqual(fixture.observations, [
    ["prepare", undefined, undefined],
    ["resolve", instance, undefined],
    ["bind", instance, fixture.memory],
  ]);
  assert.throws(() => bindInteropExecutionInstance({
    preparation: fixture.preparation,
    instance: {},
  }), /another instance/);

  closeInteropExecution({ preparation: fixture.preparation });
  closeInteropExecution({ preparation: fixture.preparation });
  assert.deepEqual(fixture.observations.at(-1), ["close"]);
  assert.equal(fixture.observations.filter(value => value[0] === "close").length, 1);
  assert.throws(() => fixture.readers.readInstance(), /closed/);
  assert.throws(() => fixture.readers.readMemory(), /closed/);
});

test("closes a preparation before binding and prevents later binding", () => {
  const fixture = create();
  closeInteropExecution({ preparation: fixture.preparation });
  assert.throws(() => bindInteropExecutionInstance({
    preparation: fixture.preparation,
    instance: {},
  }), /another instance/);
  assert.deepEqual(fixture.observations.at(-1), ["close"]);
});

test("makes resolution and binding failures terminal but still closable", () => {
  for (const mutation of [
    { resolveMemory() { throw new Error("resolve"); } },
    { bindInstance() { throw new Error("bind"); } },
  ]) {
    const fixture = create(mutation);
    assert.throws(() => bindInteropExecutionInstance({
      preparation: fixture.preparation,
      instance: {},
    }), /resolve|bind/);
    assert.throws(() => bindInteropExecutionInstance({
      preparation: fixture.preparation,
      instance: {},
    }), /another instance/);
    closeInteropExecution({ preparation: fixture.preparation });
    assert.deepEqual(fixture.observations.at(-1), ["close"]);
  }
});

test("rejects reentrant close while binding", () => {
  let preparation;
  const fixture = create({
    bindInstance() {
      closeInteropExecution({ preparation });
      return {};
    },
  });
  preparation = fixture.preparation;
  assert.throws(() => bindInteropExecutionInstance({
    preparation,
    instance: {},
  }), /changed state/);
});

test("keeps deferred readers available during exactly-once close", () => {
  let preparation;
  let readers;
  let closes = 0;
  preparation = prepareInteropExecution({
    createImports(value) { readers = value; return {}; },
    resolveMemory() { return {}; },
    bindInstance() { return {}; },
    close() {
      closes++;
      assert.deepEqual(readers.readInstance(), { id: 1 });
      assert.deepEqual(readers.readMemory(), {});
      closeInteropExecution({ preparation });
    },
  });
  bindInteropExecutionInstance({ preparation, instance: { id: 1 } });
  closeInteropExecution({ preparation });
  assert.equal(closes, 1);
});

test("revokes deferred readers even when close fails", () => {
  const fixture = create({ close() { throw new Error("close"); } });
  assert.throws(() => closeInteropExecution({
    preparation: fixture.preparation,
  }), /close/);
  closeInteropExecution({ preparation: fixture.preparation });
  assert.throws(() => fixture.readers.readInstance(), /closed/);
});

test("rejects invalid preparation requests and dependency products", () => {
  const valid = {
    bindInstance() {},
    close() {},
    createImports() { return {}; },
    resolveMemory() {},
  };
  for (const request of [null, 1, [], {}, { ...valid, extra() {} },
    Object.create(valid)]) {
    assert.throws(() => prepareInteropExecution(request), TypeError);
  }
  const withSymbol = { ...valid, [Symbol("invalid")]: true };
  assert.throws(() => prepareInteropExecution(withSymbol), TypeError);
  const withAccessor = { ...valid };
  Object.defineProperty(withAccessor, "close", { get: () => () => {}, enumerable: true });
  assert.throws(() => prepareInteropExecution(withAccessor), TypeError);
  assert.throws(() => prepareInteropExecution({
    bindInstance() {},
    close() {},
    createImports() { return {}; },
    wrongName() {},
  }), /shape/);
  for (const name of Object.keys(valid)) {
    assert.throws(() => prepareInteropExecution({ ...valid, [name]: null }), /action/);
  }
  for (const imports of [null, [], () => {}]) {
    assert.throws(() => prepareInteropExecution({
      ...valid,
      createImports() { return imports; },
    }), /imports/);
  }
  assert.throws(() => prepareInteropExecution({
    ...valid,
    createImports() { return Object.create({ inherited: true }); },
  }), /plain data/);
  const importsWithSymbol = { [Symbol("invalid")]: true };
  assert.throws(() => prepareInteropExecution({
    ...valid,
    createImports() { return importsWithSymbol; },
  }), /imports/);
  const importsWithAccessor = {};
  Object.defineProperty(importsWithAccessor, "value", { get: () => 1, enumerable: true });
  assert.throws(() => prepareInteropExecution({
    ...valid,
    createImports() { return importsWithAccessor; },
  }), /plain data/);

  const nullPrototypeRequest = Object.assign(Object.create(null), valid, {
    createImports() { return Object.create(null); },
  });
  closeInteropExecution({ preparation: prepareInteropExecution(nullPrototypeRequest) });
});

test("rejects invalid bind and close requests", () => {
  const fixture = create();
  for (const request of [null, {}, { preparation: fixture.preparation },
    { preparation: fixture.preparation, instance: {}, extra: true }]) {
    assert.throws(() => bindInteropExecutionInstance(request), TypeError);
  }
  assert.throws(() => bindInteropExecutionInstance({
    preparation: {},
    instance: {},
  }), /invalid/);
  assert.throws(() => bindInteropExecutionInstance({
    preparation: null,
    instance: {},
  }), /invalid/);
  assert.throws(() => bindInteropExecutionInstance({
    preparation: fixture.preparation,
    instance: null,
  }), /instance/);
  assert.throws(() => bindInteropExecutionInstance({
    preparation: fixture.preparation,
    instance: 1,
  }), /instance/);

  for (const request of [null, {}, { preparation: fixture.preparation, extra: true }]) {
    assert.throws(() => closeInteropExecution(request), TypeError);
  }
  assert.throws(() => closeInteropExecution({ preparation: {} }), /invalid/);
});
