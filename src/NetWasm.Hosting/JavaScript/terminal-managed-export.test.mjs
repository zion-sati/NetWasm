import assert from 'node:assert/strict';
import test from 'node:test';
import { NetWasmManagedError, managedExceptionBrand } from "./managed-errors.mjs";
import { createTerminalManagedExport } from "./terminal-managed-export.mjs";

const unexpectedCleanup = () => assert.fail('No exception cleanup should run');

test('Terminal export forwards arguments and result without reading events', async () => {
  const result = {};
  const invoke = createTerminalManagedExport('run', (...args) => {
    assert.deepEqual(args, [41, 2n]);
    return result;
  }, () => assert.fail('No event should be read on success'), unexpectedCleanup);
  assert.equal(await invoke(41, 2n), result);
});

test('Terminal export preserves ordinary host failures without reading events', async () => {
  const failure = new Error('host failure');
  const invoke = createTerminalManagedExport('run', () => { throw failure; },
    () => assert.fail('No event should be read on host failure'), unexpectedCleanup);
  await assert.rejects(invoke, error => error === failure);
});

for (const event of [null, undefined]) {
  test(`Terminal export preserves an unreported Wasm trap: ${event}`, async () => {
    const failure = new WebAssembly.RuntimeError('ordinary trap');
    let reads = 0;
    const invoke = createTerminalManagedExport('run', () => { throw failure; }, () => {
      reads++;
      return event;
    }, unexpectedCleanup);
    await assert.rejects(invoke, error => error === failure);
    assert.equal(reads, 1);
  });
}

for (const typeId of [1, 0x7fffffff]) {
  test(`Terminal export reports a managed failure with its cause: ${typeId}`, async () => {
    const failure = new WebAssembly.RuntimeError('terminal trap');
    const events = [{ typeId }];
    let cleanups = 0;
    const invoke = createTerminalManagedExport('run', () => { throw failure; }, () => events.pop(),
      () => { cleanups++; });
    await assert.rejects(invoke, error => {
      assert.ok(error instanceof NetWasmManagedError);
      assert.equal(error[managedExceptionBrand], true);
      assert.equal(error.managedType, typeId);
      assert.equal(error.exportName, 'run');
      assert.equal(error.cause, failure);
      return true;
    });
    assert.equal(events.length, 0);
    assert.equal(cleanups, 1);
    await assert.rejects(invoke, error => error === failure);
    assert.equal(cleanups, 1);
  });
}

for (const typeId of [undefined, 1.5, 0, -1, 0x80000000]) {
  test(`Terminal export rejects invalid event type identity: ${typeId}`, async () => {
    const invoke = createTerminalManagedExport('run', () => {
      throw new WebAssembly.RuntimeError('terminal trap');
    }, () => ({ typeId }), unexpectedCleanup);
    await assert.rejects(invoke, /positive managed type ID/);
  });
}

for (const name of [null, '']) {
  test(`Terminal export rejects invalid names: ${name}`, () => {
    assert.throws(() => createTerminalManagedExport(name, () => {}, () => {}), /export name/);
  });
}
for (const [invoke, consume, clear] of [
  [null, () => {}, () => {}], [() => {}, null, () => {}], [() => {}, () => {}, null],
]) {
  test('Terminal export rejects missing capabilities before invocation', () => {
    assert.throws(() => createTerminalManagedExport('run', invoke, consume, clear), /are required/);
  });
}

test('Terminal export preserves a cleanup failure instead of claiming managed recovery', async () => {
  const cleanupFailure = new Error('cleanup failed');
  const invoke = createTerminalManagedExport('run', () => {
    throw new WebAssembly.RuntimeError('terminal trap');
  }, () => ({ typeId: 1 }), () => { throw cleanupFailure; });
  await assert.rejects(invoke, error => error === cleanupFailure);
});

test('Terminal export reports a managed failure an asynchronous export rejects with', async () => {
  const failure = new WebAssembly.RuntimeError('terminal trap');
  const invoke = createTerminalManagedExport('run', async () => { throw failure; },
    () => ({ typeId: 7 }), () => {});
  await assert.rejects(invoke, error => error instanceof NetWasmManagedError
    && error.managedType === 7 && error.cause === failure);
});
