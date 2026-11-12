import assert from "node:assert/strict";
import test from "node:test";
import { createComponentExecutionStrategy } from "./component-execution-strategy.mjs";
import { commandExecutionContract } from "./execution-contracts.mjs";
import { executeWithProgramSidecars } from "./sidecar-execution.mjs";
import { materializeProgramSidecars } from "./program-sidecar-materializer.mjs";
import { selectProgramSidecars } from "./program-sidecar-selector.mjs";
import { timeZoneContainer } from "./timezone-sidecar-zones.test.mjs";
import { createWasiFilesystem } from "./wasi-filesystem.mjs";

const bytes = timeZoneContainer([["Australia/Melbourne", 1]]);
const digest = "a".repeat(64);
const guestDirectory = "/netwasm-sidecars";
const guestName = "program.wasm.tz-info";
const typesModule = "wasi:filesystem/types@0.3.2";
const preopensModule = "wasi:filesystem/preopens@0.3.2";

async function readAll([stream, future]) {
  const chunks = [];
  for await (const chunk of stream) chunks.push(...chunk);
  return { bytes: Uint8Array.from(chunks), result: await future };
}

function fixture(timeZone = "Australia/Melbourne") {
  const calls = [];
  const selection = selectProgramSidecars({
    runtimeFeatures: ["local-time"],
    artifacts: [
      { relativePath: "program.wasm", role: "application", mediaType: "application/wasm", sha256: digest, schemaVersion: null },
      { relativePath: "program.wasm.tz-info", role: "timezone-data", mediaType: "application/octet-stream", sha256: digest, schemaVersion: 1 },
    ],
    environment: [{ name: "TZ", value: timeZone }],
  });
  let provider;
  const request = {
    selection,
    createFilesystem() {
      calls.push("filesystem");
      provider = createWasiFilesystem(Object.freeze({ hostFiles: null, preopens: Object.freeze([]) }));
      return {
        ...provider,
        mountReadOnlyFile(value) {
          calls.push("mount");
          const release = provider.mountReadOnlyFile(value);
          return () => {
            calls.push("mount-release");
            release();
          };
        },
        async dispose() {
          calls.push("filesystem-release");
          await provider.dispose();
        },
      };
    },
    createMaterializer: mountReadOnlyFile => value => materializeProgramSidecars({
      ...value,
      readArtifact: async artifact => {
        assert.equal(artifact.relativePath, "program.wasm.tz-info");
        calls.push("read");
        return bytes;
      },
      hashBytes: async value => { assert.deepEqual(value, bytes); calls.push("hash"); return digest; },
      mountReadOnlyFile,
    }),
    execute: null,
    releaseActions: [{
      code: "host.consumer", message: "Could not release the consumer resources.",
      release() {
        calls.push("caller-release");
        assert.throws(() => provider.preopens.getDirectories(), /disposed/);
      },
    }],
  };
  async function openTimeZone(preopens) {
    const entry = preopens.getDirectories().find(([, path]) => path === guestDirectory);
    assert.ok(entry);
    const opened = await entry[0].openAt({}, guestName, {}, { read: true });
    assert.equal(opened.tag, "ok");
    return opened.val;
  }
  return { calls, request, openTimeZone };
}

function strategy(f, instantiate) {
  return createComponentExecutionStrategy(async () => {
    f.calls.push("load");
    return {
      adapter: { contractKey: commandExecutionContract, instantiate },
      loadCoreModule() {},
    };
  });
}

function execute(f, executeComponent) {
  f.request.execute = ({ filesystem, signal }) => executeComponent({
    artifacts: [], contractKey: commandExecutionContract, signal,
    imports: { [typesModule]: filesystem.types, [preopensModule]: filesystem.preopens },
  });
  return executeWithProgramSidecars(f.request);
}

test("a command reads the verified time-zone data before the host unmounts it", async () => {
  const f = fixture();
  const outcome = await execute(f, strategy(f, ({ imports }) => {
    f.calls.push("instantiate");
    return {
      command: {
        async run() {
          f.calls.push("run");
          const file = await f.openTimeZone(imports[preopensModule]);
          const read = await readAll(file.readViaStream(0n));
          assert.deepEqual(read.bytes, bytes);
          assert.deepEqual(read.result, { tag: "ok", val: undefined });
          return 29;
        },
      },
    };
  }));
  assert.equal(outcome.completionKind, "normal");
  assert.equal(outcome.exitCode, 29);
  assert.deepEqual(f.calls, ["filesystem", "read", "hash", "mount", "load", "instantiate", "run",
    "mount-release", "filesystem-release", "caller-release"]);
});

test("component loading and instantiation failures both release the mounted sidecar", async () => {
  for (const phase of ["load", "instantiate"]) {
    const f = fixture();
    const executeComponent = createComponentExecutionStrategy(async () => {
      f.calls.push("load");
      if (phase === "load") throw new Error("private loader detail");
      return {
        adapter: {
          contractKey: commandExecutionContract,
          async instantiate({ imports }) {
            f.calls.push("instantiate");
            await f.openTimeZone(imports[preopensModule]);
            throw new Error("private instantiation detail");
          },
        },
        loadCoreModule() {},
      };
    });
    const outcome = await execute(f, executeComponent);
    assert.equal(outcome.completionKind, "hostFailure");
    assert.equal(outcome.primaryFailure.code, phase === "load" ? "host.component-load" : "host.component-instantiate");
    assert.deepEqual(f.calls.slice(-3), ["mount-release", "filesystem-release", "caller-release"]);
    assert.equal(outcome.primaryFailure.detail,
      phase === "load" ? "Error: private loader detail" : "Error: private instantiation detail");
  }
});

test("provider disposal recovers a mount acquired before materialization fails or is cancelled", async () => {
  for (const cancelled of [false, true]) {
    const f = fixture();
    const controller = new AbortController();
    let opened;
    let scopedFilesystem;
    const create = f.request.createFilesystem;
    f.request.createFilesystem = () => { scopedFilesystem = create(); return scopedFilesystem; };
    const materializer = f.request.createMaterializer;
    f.request.createMaterializer = mount => async request => {
      await materializer(mount)(request);
      opened = await f.openTimeZone(scopedFilesystem.preopens);
      if (cancelled) controller.abort();
      throw new Error("private post-mount failure");
    };
    f.request.signal = controller.signal;
    f.request.execute = () => assert.fail("execution entered after materialization failure");
    const outcome = await executeWithProgramSidecars(f.request);
    assert.equal(outcome.completionKind, cancelled ? "callerCancellation" : "hostFailure");
    assert.equal(outcome.primaryFailure.code, cancelled ? "caller.cancelled" : "host.sidecar-materialize");
    assert.deepEqual(f.calls, ["filesystem", "read", "hash", "mount", "filesystem-release", "caller-release"]);
    assert.deepEqual(await opened.stat(), { tag: "err", val: { tag: "bad-descriptor" } });
  }
});

test("UTC still executes the component without sidecar transport or mount", async () => {
  const f = fixture("UTC");
  const outcome = await execute(f, strategy(f, ({ imports }) => {
    assert.deepEqual(imports[preopensModule].getDirectories(), []);
    return { command: { run: async () => 0 } };
  }));
  assert.equal(outcome.exitCode, 0);
  assert.deepEqual(f.calls, ["filesystem", "load", "filesystem-release", "caller-release"]);
});
