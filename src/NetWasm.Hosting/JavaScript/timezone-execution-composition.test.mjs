import assert from "node:assert/strict";
import test from "node:test";
import { createComponentExecutionStrategy } from "./component-execution-strategy.mjs";
import { processExecutionContract } from "./execution-contracts.mjs";
import { createPreview2Filesystem } from "./preview2-filesystem.mjs";
import { resourceDisposeSymbol } from "./resource-disposal.mjs";
import { executeWithTimeZoneResources } from "./timezone-execution.mjs";
import { materializeTimeZoneSidecar } from "./timezone-sidecar-materializer.mjs";
import { selectTimeZoneSidecar } from "./timezone-sidecar-selector.mjs";

const bytes = Uint8Array.of(11, 22, 33);
const digest = "a".repeat(64);
const guestDirectory = "/netwasm-timezones";
const guestName = "netwasm-timezones.nwtz";
const typesModule = "wasi:filesystem/types@0.2.0";
const preopensModule = "wasi:filesystem/preopens@0.2.0";

function fixture(timeZone = "Australia/Melbourne") {
  const calls = [];
  let openedFile;
  class Descriptor {
    openAt() { return new Descriptor(); }
    read() { return [Uint8Array.of(7), true]; }
    [resourceDisposeSymbol]() { calls.push(this === root ? "consumer-root-release" : "consumer-file-release"); }
  }
  const root = new Descriptor();
  const source = { types: { Descriptor }, preopens: { getDirectories: () => [[root, "/consumer"]] } };
  const selection = selectTimeZoneSidecar({
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
      provider = createPreview2Filesystem({ filesystem: source });
      return {
        ...provider,
        mountReadOnlyFile(value) {
          calls.push("mount");
          const release = provider.mountReadOnlyFile(value);
          return () => {
            calls.push("mount-release");
            release();
            if (openedFile) assert.throws(() => openedFile.read(1n, 0n), error => error === "bad-descriptor");
          };
        },
        dispose() { calls.push("filesystem-release"); provider.dispose(); },
      };
    },
    createMaterializer: mountReadOnlyFile => value => materializeTimeZoneSidecar({
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
        assert.deepEqual(root.read(), [Uint8Array.of(7), true]);
        assert.equal(calls.includes("consumer-root-release"), false);
      },
    }],
  };
  function openTimeZone(filesystem) {
    const entries = filesystem.preopens.getDirectories();
    const entry = entries.find(([, path]) => path === guestDirectory);
    assert.ok(entry);
    openedFile = entry[0].openAt({}, guestName, {}, { read: true });
    assert.deepEqual(openedFile.read(3n, 0n), [bytes, true]);
    return openedFile;
  }
  return { calls, request, root, openTimeZone };
}

test("component execution sees verified timezone data and finishes reactor cleanup before unmounting", async () => {
  const f = fixture();
  const executeComponent = createComponentExecutionStrategy(async () => {
    f.calls.push("load");
    return {
      adapter: {
        contractKey: processExecutionContract,
        instantiate({ imports }) {
          f.calls.push("instantiate");
          const fs = { types: imports[typesModule], preopens: imports[preopensModule] };
          const file = f.openTimeZone(fs);
          const [[consumer]] = fs.preopens.getDirectories();
          consumer.openAt({}, "consumer-file", {}, { read: true });
          const reactor = imports["netwasm:runtime/reactor-host"];
          return {
            process: {
              start() {
                reactor.watch({
                  block: () => new Promise(() => {}),
                  dispose() {
                    f.calls.push("reactor-release");
                    assert.deepEqual(file.read(3n, 0n), [bytes, true]);
                  },
                }, 1);
                return 1;
              },
              status: () => 1,
              exitCode: () => 29,
              complete() {
                f.calls.push("complete");
                assert.deepEqual(file.read(3n, 0n), [bytes, true]);
              },
            },
            reactorGuest: { wake() {} },
          };
        },
      },
      loadCoreModule() {},
    };
  });
  f.request.execute = ({ filesystem, signal }) => executeComponent({
    artifacts: [], contractKey: processExecutionContract, signal,
    imports: { [typesModule]: filesystem.types, [preopensModule]: filesystem.preopens },
  });
  const outcome = await executeWithTimeZoneResources(f.request);
  assert.equal(outcome.completionKind, "normal");
  assert.equal(outcome.exitCode, 29);
  assert.deepEqual(f.calls, ["filesystem", "read", "hash", "mount", "load", "instantiate",
    "complete", "reactor-release", "mount-release", "filesystem-release", "consumer-file-release", "caller-release"]);
});

test("component loading and instantiation failures both release the mounted sidecar", async () => {
  for (const phase of ["load", "instantiate"]) {
    const f = fixture();
    const executeComponent = createComponentExecutionStrategy(async () => {
      f.calls.push("load");
      if (phase === "load") throw new Error("private loader detail");
      return {
        adapter: {
          contractKey: processExecutionContract,
          instantiate({ imports }) {
            f.calls.push("instantiate");
            f.openTimeZone({ preopens: imports[preopensModule] });
            throw new Error("private instantiation detail");
          },
        },
        loadCoreModule() {},
      };
    });
    f.request.execute = ({ filesystem }) => executeComponent({
      artifacts: [], contractKey: processExecutionContract,
      imports: { [typesModule]: filesystem.types, [preopensModule]: filesystem.preopens },
    });
    const outcome = await executeWithTimeZoneResources(f.request);
    assert.equal(outcome.completionKind, "hostFailure");
    assert.equal(outcome.primaryFailure.code, phase === "load" ? "host.component-load" : "host.component-instantiate");
    assert.deepEqual(f.calls.slice(-3), ["mount-release", "filesystem-release", "caller-release"]);
    assert.doesNotMatch(JSON.stringify(outcome), /private/);
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
      opened = f.openTimeZone(scopedFilesystem);
      if (cancelled) controller.abort();
      throw new Error("private post-mount failure");
    };
    f.request.signal = controller.signal;
    f.request.execute = () => assert.fail("execution entered after materialization failure");
    const outcome = await executeWithTimeZoneResources(f.request);
    assert.equal(outcome.completionKind, cancelled ? "callerCancellation" : "hostFailure");
    assert.equal(outcome.primaryFailure.code, cancelled ? "caller.cancelled" : "host.timezone-materialize");
    assert.deepEqual(f.calls, ["filesystem", "read", "hash", "mount", "filesystem-release", "caller-release"]);
    assert.throws(() => opened.read(1n, 0n), error => error === "bad-descriptor");
  }
});

test("UTC still executes the component without sidecar transport or mount", async () => {
  const f = fixture("UTC");
  const executeComponent = createComponentExecutionStrategy(async () => ({
    adapter: {
      contractKey: processExecutionContract,
      instantiate({ imports }) {
        assert.deepEqual(imports[preopensModule].getDirectories().map(([, path]) => path), ["/consumer"]);
        return {
          process: { start: () => 1, status: () => 1, exitCode: () => 0, complete() {} },
          reactorGuest: { wake() {} },
        };
      },
    },
    loadCoreModule() {},
  }));
  f.request.execute = ({ filesystem }) => executeComponent({
    artifacts: [], contractKey: processExecutionContract,
    imports: { [typesModule]: filesystem.types, [preopensModule]: filesystem.preopens },
  });
  assert.equal((await executeWithTimeZoneResources(f.request)).exitCode, 0);
  assert.deepEqual(f.calls, ["filesystem", "filesystem-release", "caller-release"]);
});
