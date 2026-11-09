import assert from "node:assert/strict";
import { createHash } from "node:crypto";
import { isAbsolute } from "node:path";
import test from "node:test";

import { createExecutionDescriptorReader } from "./execution-descriptor-reader.mjs";
import { createExecutionDescriptorValidator } from "./execution-descriptor-validator.mjs";
import { createExecutionRequestReader } from "./execution-request-reader.mjs";
import { createExecutionRequestValidator } from "./execution-request-validator.mjs";
import { createStandaloneChannels, readStandaloneArguments } from "./local-standalone-channels.mjs";
import { selectProviderKind } from "./provider-kind-selector.mjs";
import { parseStrictJson } from "./strict-json-reader.mjs";

const encoder = new TextEncoder();
const invocation = Object.freeze({
  hostExecutablePath: "/usr/bin/node",
  launcherPath: "/app/publish/netwasm.host.mjs",
  preopens: Object.freeze([]),
});
const readDescriptor = createExecutionDescriptorReader({
  parseJson: parseStrictJson,
  validate: createExecutionDescriptorValidator({ isAbsoluteHostPath: isAbsolute }),
});
const readRequest = createExecutionRequestReader({
  parseJson: parseStrictJson,
  validate: createExecutionRequestValidator({ hostDirectories: true, isAbsoluteHostPath: isAbsolute, selectProviderKind }),
});

test("makes dotnet run's channels from the deployment beside the launcher", async () => {
  const bytes = encoder.encode(JSON.stringify(manifest("bin/hello.wasm")));
  const reads = [];
  const read = createStandaloneChannels({
    hashBytes: value => createHash("sha256").update(value).digest("hex"),
    async readFile(path) { reads.push(path); return bytes; },
  });
  assert.equal(Object.isFrozen(read), true);
  const channels = await read(invocation);
  assert.equal(Object.isFrozen(channels), true);
  assert.deepEqual(reads, ["/app/publish/deployment.json"]);
  const sha256 = createHash("sha256").update(bytes).digest("hex");
  assert.deepEqual({ ...readDescriptor(channels.descriptorText) }, {
    schemaVersion: 1,
    buildFingerprint: "1".repeat(64),
    deploymentManifestPath: "/app/publish/deployment.json",
    deploymentManifestSha256: sha256,
    hostingVersion: "0.12.0",
    hostExecutablePath: "/usr/bin/node",
    launcherPath: "/app/publish/netwasm.host.mjs",
  });
  const request = readRequest(channels.requestText);
  assert.equal(request.deploymentManifestSha256, sha256);
  assert.equal(request.buildFingerprint, "1".repeat(64));
  assert.deepEqual(request.arguments, ["hello"]);
  assert.deepEqual(request.environment, []);
  assert.deepEqual(request.applicationImports, []);
  assert.deepEqual({ ...request.grants, clocks: [...request.grants.clocks] }, {
    environment: [], preopens: [], network: "allowAll", clocks: ["wall", "monotonic"], randomness: true, temporaryDirectory: false, workingDirectory: false,
  });
});

test("names a program after an application without a .wasm extension as it is", async () => {
  const read = createStandaloneChannels({
    hashBytes: () => "2".repeat(64),
    readFile: async () => encoder.encode(JSON.stringify(manifest("tool"))),
  });
  assert.deepEqual(readRequest((await read(invocation)).requestText).arguments, ["tool"]);
});

test("rejects unreadable or invalid deployments", async () => {
  const hashBytes = () => "2".repeat(64);
  await assert.rejects(
    () => createStandaloneChannels({ hashBytes, readFile: async () => "text" })(invocation),
    /returned invalid bytes/);
  await assert.rejects(
    () => createStandaloneChannels({ hashBytes, readFile: async () => encoder.encode("{}") })(invocation),
    /deployment manifest/);
  await assert.rejects(
    () => createStandaloneChannels({
      hashBytes, readFile: async () => { throw new Error("ENOENT: no deployment.json"); },
    })(invocation),
    /ENOENT/);
});

test("rejects malformed options and invocations", async () => {
  const actions = { hashBytes() {}, readFile() {} };
  for (const value of [null, 1, [], {}, { ...actions, extra() {} },
    Object.create(actions), { ...actions, [Symbol("extra")]: true }]) {
    assert.throws(() => createStandaloneChannels(value), TypeError);
  }
  const accessor = { ...actions };
  Object.defineProperty(accessor, "readFile", { enumerable: true, get: () => () => {} });
  assert.throws(() => createStandaloneChannels(accessor), TypeError);
  assert.throws(() => createStandaloneChannels({ ...actions, hashBytes: 1 }), /'hashBytes' action/);
  const read = createStandaloneChannels(actions);
  for (const value of [null, {}, { ...invocation, extra: 1 }, { ...invocation, preopens: null }]) {
    await assert.rejects(() => read(value), TypeError);
  }
  for (const path of [null, "relative.mjs", "/app/../app/netwasm.host.mjs", "/app/a\0b"]) {
    await assert.rejects(() => read({ ...invocation, launcherPath: path }), /launcher path/);
    await assert.rejects(() => read({ ...invocation, hostExecutablePath: path }), /host executable/);
  }
});

test("a plain launch mounts the directories named with --dir before the program's arguments", async () => {
  const parsed = readStandaloneArguments(
    ["--dir", ".", "--dir", "data::/data", "--dir", "/srv/logs", "--", "--dir", "x"], "/work/app", "linux");
  assert.equal(Object.isFrozen(parsed), true);
  assert.deepEqual(parsed.arguments, ["--dir", "x"]);
  assert.deepEqual(parsed.preopens, [
    { hostPath: "/work/app", guestPath: "/work/app", access: "readWrite" },
    { hostPath: "/work/app/data", guestPath: "/data", access: "readWrite" },
    { hostPath: "/srv/logs", guestPath: "/srv/logs", access: "readWrite" },
  ]);
  assert.deepEqual(readStandaloneArguments(["a", "--dir", "b"], "/w", "linux"),
    { arguments: ["a", "--dir", "b"], preopens: [] });
  assert.deepEqual(readStandaloneArguments([], "/w", "linux"), { arguments: [], preopens: [] });

  const read = createStandaloneChannels({
    hashBytes: () => "2".repeat(64),
    readFile: async () => encoder.encode(JSON.stringify(manifest("tool"))),
  });
  const request = readRequest((await read({ ...invocation, preopens: parsed.preopens })).requestText);
  assert.deepEqual(request.grants.preopens.map(preopen => preopen.guestPath), ["/work/app", "/data", "/srv/logs"]);

  for (const values of [["--dir"], ["--dir", ""], ["--dir", "a\0"], ["--dir", "a::relative"],
    ["--dir", "a::/same", "--dir", "b::/same"]]) {
    assert.throws(() => readStandaloneArguments(values, "/w", "linux"), /--dir/);
  }
  for (const [values, directory, platform] of [[null, "/w", "linux"], [[], 1, "linux"], [[], "/w", null]]) {
    assert.throws(() => readStandaloneArguments(values, directory, platform), /are required/);
  }
});

function manifest(application) {
  const digest = "1".repeat(64);
  return {
    artifacts: [{
      mediaType: "application/wasm", relativePath: application, role: "application",
      schemaVersion: null, sha256: digest,
    }],
    buildFingerprint: digest,
    deploymentKind: "component",
    executionContract: "wasi-command@0.3.2",
    exports: [],
    featureSet: "default",
    profile: "netwasm0.1",
    requiredImportModules: [],
    requiredImports: [],
    runtimeFeatures: [],
    schemaVersion: 1,
    semanticBuildId: digest,
    target: "wasm32",
    versions: {
      compiler: "0.12.0", hosting: "0.12.0", runtime: "0.12.0", runtimeAbi: "1", sdk: "0.12.0",
      toolchain: "0.12.0",
    },
  };
}
