import assert from "node:assert/strict";
import test from "node:test";
import { executeWithTimeZoneResources } from "./timezone-execution.mjs";
import { failedExecutionResult, normalExecutionResult } from "./execution-result.mjs";

const release = (code, action) => ({ code, message: `Could not release ${code}.`, release: action });

function fixture() {
  const calls = [];
  const selection = Object.freeze({ selected: true });
  const filesystem = {
    types: { Descriptor: class {} },
    preopens: { getDirectories() { return []; } },
    mountReadOnlyFile(value) {
      assert.equal(this, filesystem);
      assert.equal(value, selection);
      calls.push("mount");
      return () => calls.push("mount-release");
    },
    dispose() {
      assert.equal(this, filesystem);
      calls.push("filesystem-release");
    },
  };
  const request = {
    selection,
    createFilesystem() { calls.push("filesystem"); return filesystem; },
    createMaterializer(mount) {
      calls.push("materializer");
      return async value => {
        calls.push("materialize");
        assert.equal(Object.isFrozen(value), true);
        assert.equal(value.selection, request.selection);
        assert.equal(value.signal, request.signal ?? null);
        return value.selection === null ? null : release("mount", mount(value.selection));
      };
    },
    async execute(value) {
      assert.equal(Object.isFrozen(value), true);
      assert.equal(value.filesystem, filesystem);
      assert.equal(value.signal, request.signal ?? null);
      calls.push("execute");
      return normalExecutionResult(17);
    },
    releaseActions: [release("caller", () => calls.push("caller-release"))],
  };
  return { calls, filesystem, request };
}

test("prepares the mount before execution and closes owned resources before the caller", async () => {
  const f = fixture();
  f.request.signal = new AbortController().signal;
  assert.deepEqual(await executeWithTimeZoneResources(f.request), normalExecutionResult(17));
  assert.deepEqual(f.calls, ["filesystem", "materializer", "materialize", "mount", "execute",
    "mount-release", "filesystem-release", "caller-release"]);
});

test("a null selection has no mount and optional caller cleanup can be omitted", async () => {
  const f = fixture();
  f.request.selection = null;
  delete f.request.releaseActions;
  const request = Object.assign(Object.create(null), f.request);
  assert.equal((await executeWithTimeZoneResources(request)).exitCode, 17);
  assert.deepEqual(f.calls, ["filesystem", "materializer", "materialize", "execute", "filesystem-release"]);
});

test("rejects malformed requests before invoking any dependencies", async () => {
  const f = fixture();
  const accessor = { ...f.request };
  Object.defineProperty(accessor, "selection", { enumerable: true, get() { assert.fail("getter ran"); } });
  const hidden = { ...f.request };
  Object.defineProperty(hidden, "selection", { enumerable: false, value: null });
  for (const value of [undefined, null, 0, [], {}, Object.create(f.request),
    { ...f.request, extra: true }, { ...f.request, [Symbol()]: true }, accessor, hidden]) {
    await assert.rejects(executeWithTimeZoneResources(value), TypeError);
    assert.deepEqual(f.calls, []);
  }
  for (const key of ["createFilesystem", "createMaterializer", "execute"]) {
    await assert.rejects(executeWithTimeZoneResources({ ...f.request, [key]: null }), TypeError);
    assert.deepEqual(f.calls, []);
  }
  for (const signal of [false, {}, { aborted: false }, { aborted: false, addEventListener() {} }]) {
    await assert.rejects(executeWithTimeZoneResources({ ...f.request, signal }), TypeError);
    assert.deepEqual(f.calls, []);
  }
  for (const releaseActions of [null, [null], [release("invalid code", () => {})]]) {
    await assert.rejects(executeWithTimeZoneResources({ ...f.request, releaseActions }), TypeError);
    assert.deepEqual(f.calls, []);
  }
});

test("snapshots caller cleanup before the filesystem factory can change the request", async () => {
  const f = fixture();
  f.request.createFilesystem = () => {
    f.request.releaseActions[0].release = () => assert.fail("replacement cleanup ran");
    f.request.releaseActions.length = 0;
    return f.filesystem;
  };
  assert.equal((await executeWithTimeZoneResources(f.request)).exitCode, 17);
  assert.equal(f.calls.at(-1), "caller-release");
});

test("filesystem acquisition failure closes the caller without attempting later stages", async () => {
  for (const result of [null, undefined, 0, {}, { dispose: false }]) {
    const f = fixture();
    f.request.createFilesystem = () => result;
    assertFailure(await executeWithTimeZoneResources(f.request), "host.filesystem-prepare", "instantiation");
    assert.deepEqual(f.calls, ["caller-release"]);
  }
  const f = fixture();
  f.request.createFilesystem = () => { throw new Error("private factory detail"); };
  assertFailure(await executeWithTimeZoneResources(f.request), "host.filesystem-prepare", "instantiation");
  assert.deepEqual(f.calls, ["caller-release"]);
});

test("captures filesystem disposal before validating the remaining provider shape", async () => {
  for (const change of [
    fs => { delete fs.types; }, fs => { fs.types.Descriptor = null; },
    fs => { delete fs.preopens; }, fs => { fs.preopens.getDirectories = null; },
    fs => { fs.mountReadOnlyFile = null; },
  ]) {
    const f = fixture();
    change(f.filesystem);
    assertFailure(await executeWithTimeZoneResources(f.request), "host.filesystem-prepare", "instantiation");
    assert.deepEqual(f.calls, ["filesystem", "filesystem-release", "caller-release"]);
  }
});

test("materializer creation failures still release the filesystem and caller", async () => {
  for (const createMaterializer of [() => null, () => { throw new Error("private factory detail"); }]) {
    const f = fixture();
    f.request.createMaterializer = createMaterializer;
    assertFailure(await executeWithTimeZoneResources(f.request), "host.timezone-materialize", "instantiation");
    assert.deepEqual(f.calls, ["filesystem", "filesystem-release", "caller-release"]);
  }
});

test("invalid or failed mount acquisition releases the provider without entering execution", async () => {
  for (const mount of [null, undefined, {}, release("invalid code", () => assert.fail("invalid cleanup ran"))]) {
    const f = fixture();
    f.request.createMaterializer = () => async () => mount;
    assertFailure(await executeWithTimeZoneResources(f.request), "host.timezone-materialize", "instantiation");
    assert.deepEqual(f.calls, ["filesystem", "filesystem-release", "caller-release"]);
  }
  const f = fixture();
  f.request.selection = null;
  f.request.createMaterializer = () => async () => release("unexpected", () => assert.fail("unowned release ran"));
  assertFailure(await executeWithTimeZoneResources(f.request), "host.timezone-materialize", "instantiation");
  assert.deepEqual(f.calls, ["filesystem", "filesystem-release", "caller-release"]);

  const throwing = fixture();
  throwing.request.createMaterializer = () => async () => { throw new Error("private transport detail"); };
  assertFailure(await executeWithTimeZoneResources(throwing.request), "host.timezone-materialize", "instantiation");
  assert.deepEqual(throwing.calls, ["filesystem", "filesystem-release", "caller-release"]);
});

test("execution exceptions and invalid outcomes close all resources with a fixed failure", async () => {
  for (const execute of [async () => null, async () => { throw new Error("private execution detail"); }]) {
    const f = fixture();
    f.request.execute = execute;
    assertFailure(await executeWithTimeZoneResources(f.request), "host.deployment-execute", "execution");
    assert.deepEqual(f.calls, ["filesystem", "materializer", "materialize", "mount",
      "mount-release", "filesystem-release", "caller-release"]);
  }
});

test("valid execution outcomes remain authoritative even if cancellation arrives late", async () => {
  const results = [normalExecutionResult(0), ...[
    "managedFailure", "managedCancellation", "callerCancellation", "contractFailure", "hostFailure",
  ].map(kind => failedExecutionResult(kind, "execution", "guest.failure", "Guest execution failed."))];
  for (const outcome of results) {
    const f = fixture();
    const controller = new AbortController();
    f.request.signal = controller.signal;
    f.request.execute = async () => { controller.abort(); return outcome; };
    assert.equal(await executeWithTimeZoneResources(f.request), outcome);
    assert.deepEqual(f.calls.slice(-3), ["mount-release", "filesystem-release", "caller-release"]);
  }
});

test("cancellation before allocation only closes caller resources", async () => {
  const f = fixture();
  const controller = new AbortController();
  controller.abort();
  f.request.signal = controller.signal;
  assertCancellation(await executeWithTimeZoneResources(f.request), "instantiation");
  assert.deepEqual(f.calls, ["caller-release"]);
});

test("cancellation between preparation stages closes exactly the acquired resources", async () => {
  for (const phase of ["filesystem", "materializer", "mount", "execute"]) {
    const f = fixture();
    const controller = new AbortController();
    f.request.signal = controller.signal;
    if (phase === "filesystem") {
      f.request.createFilesystem = () => { controller.abort(); return f.filesystem; };
    } else if (phase === "materializer") {
      f.request.createMaterializer = () => { controller.abort(); return () => assert.fail("materialization ran"); };
    } else if (phase === "mount") {
      const create = f.request.createMaterializer;
      f.request.createMaterializer = mount => async value => {
        const action = await create(mount)(value);
        controller.abort();
        return action;
      };
    } else {
      f.request.execute = async () => { controller.abort(); throw new Error("private abort detail"); };
    }
    assertCancellation(await executeWithTimeZoneResources(f.request), phase === "execute" ? "execution" : "instantiation");
    const expected = phase === "filesystem" || phase === "materializer"
      ? ["filesystem-release", "caller-release"]
      : ["mount-release", "filesystem-release", "caller-release"];
    assert.deepEqual(f.calls.slice(-expected.length), expected);
    assert.equal(f.calls.includes("execute"), false);
    assert.equal(f.calls.includes("mount"), phase === "mount" || phase === "execute");
  }
});

test("cleanup failures do not prevent later cleanup and preserve failure precedence", async () => {
  for (const primary of [null, failedExecutionResult("managedFailure", "execution", "guest.failure", "Guest failed.")]) {
    const f = fixture();
    const fail = name => () => { f.calls.push(name); throw new Error("private cleanup detail"); };
    f.request.createMaterializer = () => async () => release("mount", fail("mount-release"));
    f.filesystem.dispose = fail("filesystem-release");
    f.request.releaseActions = [release("caller", fail("caller-release"))];
    f.request.execute = async () => primary ?? normalExecutionResult(0);
    const outcome = await executeWithTimeZoneResources(f.request);
    assert.equal(outcome.completionKind, primary === null ? "hostFailure" : "managedFailure");
    assert.equal(outcome.primaryFailure.code, primary === null ? "mount" : "guest.failure");
    assert.deepEqual(outcome.cleanupFailures.map(failure => failure.code),
      primary === null ? ["host.filesystem-cleanup", "caller"] : ["mount", "host.filesystem-cleanup", "caller"]);
    assert.deepEqual(f.calls.slice(-3), ["mount-release", "filesystem-release", "caller-release"]);
    assert.doesNotMatch(JSON.stringify(outcome), /private/);
  }
});

function assertFailure(outcome, code, phase) {
  assert.equal(outcome.completionKind, "hostFailure");
  assert.equal(outcome.primaryFailure.code, code);
  assert.equal(outcome.primaryFailure.phase, phase);
  assert.deepEqual(outcome.cleanupFailures, []);
  assert.doesNotMatch(JSON.stringify(outcome), /private/);
}

function assertCancellation(outcome, phase) {
  assert.equal(outcome.completionKind, "callerCancellation");
  assert.equal(outcome.primaryFailure.code, "caller.cancelled");
  assert.equal(outcome.primaryFailure.phase, phase);
}
