import assert from "node:assert/strict";
import { createHash } from "node:crypto";
import { mkdir, mkdtemp, readFile, realpath, rm, symlink, writeFile } from "node:fs/promises";
import { tmpdir } from "node:os";
import { join } from "node:path";
import test from "node:test";

import { createLocalNodeNetworkLoader } from "./local-node-network-loader.mjs";

const shim = "node_modules/@bytecodealliance/preview3-shim";
const entryPath = `${shim}/dist/nodejs/http.js`;
const entrySource = [
  'import { tag } from "./http/client.js";',
  "export const types = { Fields: class Fields {}, tag };",
  "export const client = { calls: 0, send(request) { this.calls++; return [request, this.calls]; } };",
].join("\n");
const socketsPath = `${shim}/dist/nodejs/sockets.js`;
const socketsSource = [
  'import { family } from "./sockets/address.js";',
  "export class TcpSocket {}",
  "export class UdpSocket {}",
  "export const types = { TcpSocket, UdpSocket, family };",
  "export const ipNameLookup = { prefix: 'resolved ', async resolveAddresses(name) { return this.prefix + name; } };",
].join("\n");
const files = {
  [`${shim}/package.json`]: '{"type":"module"}',
  [`${shim}/dist/nodejs/http/client.js`]: 'export const tag = "client";',
};
const socketFiles = {
  [`${shim}/dist/nodejs/sockets/address.js`]: 'export const family = "ipv4";',
};

test("loads and binds the deployment's verified Node.js HTTP client", async () => {
  await withDeployment({}, async ({ manifestPath, load }) => {
    const network = await load(manifestPath);
    assert.equal(Object.isFrozen(network), true);
    assert.deepEqual(Object.keys(network.modules), ["wasi:http/types@0.3.2", "wasi:http/client@0.3.2"]);
    assert.equal(network.modules["wasi:http/types@0.3.2"].tag, "client");
    assert.equal(typeof network.modules["wasi:http/types@0.3.2"].Fields, "function");
    const { send } = network.modules["wasi:http/client@0.3.2"];
    assert.deepEqual(send("request"), ["request", 1]);
  });
});

test("loads and binds the deployment's verified Node.js sockets beside its HTTP client", async () => {
  await withDeployment({ artifacts: () => [...defaultArtifacts(), ...socketArtifacts()] },
    async ({ manifestPath, load }) => {
      const network = await load(manifestPath);
      assert.deepEqual(Object.keys(network.modules), [
        "wasi:http/types@0.3.2", "wasi:http/client@0.3.2",
        "wasi:sockets/types@0.3.2", "wasi:sockets/ip-name-lookup@0.3.2",
      ]);
      const types = network.modules["wasi:sockets/types@0.3.2"];
      assert.equal(Object.isFrozen(types), true);
      assert.equal(typeof types.TcpSocket, "function");
      assert.equal(typeof types.UdpSocket, "function");
      assert.equal(types.family, "ipv4");
      const { resolveAddresses } = network.modules["wasi:sockets/ip-name-lookup@0.3.2"];
      assert.equal(await resolveAddresses("example.test"), "resolved example.test");
    });
});

test("loads sockets alone for a deployment that imports no HTTP", async () => {
  const artifacts = () => [
    ...socketArtifacts(),
    artifact(`${shim}/package.json`, "node-network-provider-file", files[`${shim}/package.json`], "application/json"),
    application(),
  ];
  await withDeployment({ artifacts }, async ({ manifestPath, load }) => {
    const network = await load(manifestPath);
    assert.deepEqual(Object.keys(network.modules),
      ["wasi:sockets/types@0.3.2", "wasi:sockets/ip-name-lookup@0.3.2"]);
  });
});

test("rejects sockets whose entry point is duplicated or whose module lacks the WASI sockets shape", async () => {
  const entry = artifact(socketsPath, "node-sockets-provider", socketsSource);
  await withDeployment({ artifacts: () => [application(), entry, { ...entry, relativePath: `${shim}/other.js` }] },
    async ({ manifestPath, load }) => {
      await assert.rejects(() => load(manifestPath), /sockets entry point is invalid/);
    });
  for (const value of [
    null,
    { types: null, ipNameLookup: { resolveAddresses() {} } },
    { types: { TcpSocket() {} }, ipNameLookup: { resolveAddresses() {} } },
    { types: { TcpSocket() {}, UdpSocket() {} }, ipNameLookup: null },
    { types: { TcpSocket() {}, UdpSocket() {} }, ipNameLookup: { resolveAddresses: 1 } },
  ]) {
    await withDeployment({ artifacts: () => [...socketArtifacts(), application()], importModule: async () => value },
      async ({ manifestPath, load }) => {
        await assert.rejects(() => load(manifestPath), /sockets is incompatible/);
      });
  }
});

test("gives a deployment without an HTTP client no network", async () => {
  await withDeployment({ artifacts: () => [application()] }, async ({ manifestPath, load }) => {
    assert.equal(await load(manifestPath), null);
  });
});

test("rejects an entry point that is duplicated or not JavaScript", async () => {
  const entry = artifact(entryPath, "node-http-provider", entrySource);
  for (const artifacts of [
    () => [application(), entry, { ...entry, relativePath: `${shim}/other.js` }],
    () => [application(), { ...entry, mediaType: "application/json" }],
  ]) {
    await withDeployment({ artifacts }, async ({ manifestPath, load }) => {
      await assert.rejects(() => load(manifestPath), /entry point is invalid/);
    });
  }
});

test("rejects a client file that changed or lies outside the deployment", async () => {
  await withDeployment({}, async ({ root, manifestPath, load }) => {
    await writeFile(join(root, `${shim}/dist/nodejs/http/client.js`), "export const tag = 1;");
    await assert.rejects(() => load(manifestPath), /client\.js' failed integrity validation/);
  });
  await withDeployment({}, async ({ root, manifestPath, load }) => {
    const outside = await mkdtemp(join(tmpdir(), "netwasm-outside-"));
    try {
      const path = join(outside, "client.js");
      await writeFile(path, files[`${shim}/dist/nodejs/http/client.js`]);
      await rm(join(root, `${shim}/dist/nodejs/http/client.js`));
      await symlink(path, join(root, `${shim}/dist/nodejs/http/client.js`));
      await assert.rejects(() => load(manifestPath), /escapes its root/);
    } finally {
      await rm(outside, { recursive: true, force: true });
    }
  });
});

test("rejects an imported client without the WASI HTTP shape", async () => {
  for (const value of [
    null,
    { types: null, client: { send() {} } },
    { types: {}, client: null },
    { types: {}, client: { send: 1 } },
    "module",
  ]) {
    await withDeployment({ importModule: async () => value }, async ({ manifestPath, load }) => {
      await assert.rejects(() => load(manifestPath), /client is incompatible/);
    });
  }
});

test("rejects invalid physical paths and file readers", async () => {
  await withDeployment({ realPath: async () => "relative" }, async ({ manifestPath, load }) => {
    await assert.rejects(() => load(manifestPath), /physical root is invalid/);
  });
  await withDeployment({ realPath: async () => 1 }, async ({ manifestPath, load }) => {
    await assert.rejects(() => load(manifestPath), /physical root is invalid/);
  });
  // A file resolving to the root itself, or to no absolute path, escapes the root.
  for (const answer of [root => root, () => 1, () => "relative"]) {
    let deploymentRoot;
    const realPath = async path => (path === deploymentRoot ? path : answer(deploymentRoot));
    await withDeployment({ realPath }, async ({ root, manifestPath, load }) => {
      deploymentRoot = root;
      await assert.rejects(() => load(manifestPath), /escapes its root/);
    });
  }
  await withDeployment({ readFile: async () => "text" }, async ({ manifestPath, load }) => {
    await assert.rejects(() => load(manifestPath), /read invalid bytes/);
  });
});

test("rejects malformed options and manifest paths", async () => {
  const actions = { hashBytes() {}, importModule() {}, readFile() {}, realPath() {} };
  for (const value of [null, 1, [], {}, { ...actions, extra() {} },
    Object.create(actions), { ...actions, [Symbol("extra")]: true }]) {
    assert.throws(() => createLocalNodeNetworkLoader(value), TypeError);
  }
  const accessor = { ...actions };
  Object.defineProperty(accessor, "readFile", { enumerable: true, get: () => () => {} });
  assert.throws(() => createLocalNodeNetworkLoader(accessor), TypeError);
  assert.throws(() => createLocalNodeNetworkLoader({ ...actions, readFile: 1 }), /'readFile' action/);
  const load = createLocalNodeNetworkLoader(actions);
  for (const path of [null, "", "relative.json", "/tmp/../tmp/deployment.json", "/tmp/a\0b"]) {
    await assert.rejects(() => load(path), /canonical and absolute/);
  }
});

function artifact(relativePath, role, text, mediaType = "text/javascript") {
  return { mediaType, relativePath, role, schemaVersion: null, sha256: sha256(Buffer.from(text)) };
}

function defaultArtifacts() {
  return [
    artifact(entryPath, "node-http-provider", entrySource),
    ...Object.entries(files).map(([path, text]) => artifact(
      path, "node-network-provider-file", text,
      path.endsWith(".json") ? "application/json" : "text/javascript")),
    application(),
  ];
}

function socketArtifacts() {
  return [
    artifact(socketsPath, "node-sockets-provider", socketsSource),
    ...Object.entries(socketFiles).map(([path, text]) => artifact(path, "node-network-provider-file", text)),
  ];
}

function application() {
  return artifact("app.wasm", "application", "wasm", "application/wasm");
}

async function withDeployment(overrides, action) {
  const root = await realpath(await mkdtemp(join(tmpdir(), "netwasm-node-network-")));
  try {
    for (const [path, text] of Object.entries({
      ...files, ...socketFiles, [entryPath]: entrySource, [socketsPath]: socketsSource, "app.wasm": "wasm",
    })) {
      await mkdir(join(root, path, ".."), { recursive: true });
      await writeFile(join(root, path), text);
    }
    const manifestPath = join(root, "deployment.json");
    await writeFile(manifestPath, JSON.stringify(manifest((overrides.artifacts ?? defaultArtifacts)())));
    const load = createLocalNodeNetworkLoader({
      hashBytes: sha256,
      importModule: overrides.importModule ?? (value => import(value)),
      readFile: overrides.readFile ?? readFile,
      realPath: overrides.realPath ?? realpath,
    });
    await action({ root, manifestPath, load });
  } finally {
    await rm(root, { recursive: true, force: true });
  }
}

function sha256(bytes) {
  return createHash("sha256").update(bytes).digest("hex");
}

function manifest(artifacts) {
  const digest = "1".repeat(64);
  return {
    artifacts,
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
    versions: { compiler: "1", hosting: "1", runtime: "1", runtimeAbi: "1", sdk: "1", toolchain: "1" },
  };
}
