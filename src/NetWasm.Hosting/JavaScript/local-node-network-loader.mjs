// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: MIT

import { dirname, isAbsolute, relative, resolve, sep } from "node:path";
import { pathToFileURL } from "node:url";

import { validateDeploymentManifest } from "./deployment-manifest-validator.mjs";
import { parseStrictJson } from "./strict-json-reader.mjs";

const factoryKeys = ["createTls", "hashBytes", "importModule", "readFile", "realPath"];
const tlsModules = ["wasi:tls/client@0.3.0-draft", "wasi:tls/types@0.3.0-draft"];
const fileRole = "node-network-provider-file";
const decoder = new TextDecoder("utf-8", { fatal: true });

// The Node.js WASI implementations a deployment carries (the SDK copies preview3-shim's into it): the wasi:http
// client when the program imports wasi:http, and wasi:sockets when it imports a socket interface. Each entry point
// becomes the modules NetWasm's WASI platform supplies for its interfaces.
const providers = Object.freeze([
  Object.freeze({
    role: "node-http-provider",
    name: "HTTP client",
    modules(http) {
      if (http === null || typeof http !== "object"
          || http.types === null || typeof http.types !== "object"
          || http.client === null || typeof http.client !== "object"
          || typeof http.client.send !== "function") {
        return null;
      }
      return {
        "wasi:http/types@0.3.2": Object.freeze({ ...http.types }),
        "wasi:http/client@0.3.2": Object.freeze({ send: http.client.send.bind(http.client) }),
      };
    },
  }),
  Object.freeze({
    role: "node-sockets-provider",
    name: "sockets",
    modules(sockets) {
      if (sockets === null || typeof sockets !== "object"
          || sockets.types === null || typeof sockets.types !== "object"
          || typeof sockets.types.TcpSocket !== "function" || typeof sockets.types.UdpSocket !== "function"
          || sockets.ipNameLookup === null || typeof sockets.ipNameLookup !== "object"
          || typeof sockets.ipNameLookup.resolveAddresses !== "function") {
        return null;
      }
      return {
        "wasi:sockets/types@0.3.2": Object.freeze({ ...sockets.types }),
        "wasi:sockets/ip-name-lookup@0.3.2": Object.freeze({
          resolveAddresses: sockets.ipNameLookup.resolveAddresses.bind(sockets.ipNameLookup),
        }),
      };
    },
  }),
]);

// Loads the Node.js WASI implementations a deployment carries after verifying each of their files against the
// deployment manifest, and returns them as the network modules NetWasm's WASI platform supplies, with NetWasm's own
// wasi:tls (createTls) when the program imports it. A deployment that needs none gets no network.
export function createLocalNodeNetworkLoader(options) {
  assertExactDataObject(options, factoryKeys, "local Node.js network loader options");
  for (const [name, value] of Object.entries(options)) {
    if (typeof value !== "function") {
      throw new TypeError(`local Node.js network loader '${name}' action is required`);
    }
  }

  return Object.freeze(async function loadNodeNetwork(manifestPath) {
    if (typeof manifestPath !== "string" || manifestPath.includes("\0")
        || !isAbsolute(manifestPath) || resolve(manifestPath) !== manifestPath) {
      throw new TypeError("local deployment manifest path must be canonical and absolute");
    }
    const manifest = validateDeploymentManifest(
      parseStrictJson(decoder.decode(await readBytes(options, manifestPath))));
    const carried = [];
    for (const provider of providers) {
      const entries = manifest.artifacts.filter(artifact => artifact.role === provider.role);
      if (entries.length === 0) continue;
      if (entries.length !== 1 || entries[0].mediaType !== "text/javascript") {
        throw new TypeError(`deployment Node.js ${provider.name} entry point is invalid`);
      }
      carried.push({ provider, entry: entries[0] });
    }
    const tls = manifest.requiredImportModules.some(module => module.startsWith("wasi:tls/"))
      ? readTlsModules(options.createTls())
      : null;
    if (carried.length === 0) return tls === null ? null : Object.freeze({ modules: tls });

    const root = dirname(manifestPath);
    const physicalRoot = await options.realPath(root);
    if (typeof physicalRoot !== "string" || !isAbsolute(physicalRoot)) {
      throw new TypeError("deployment physical root is invalid");
    }
    const files = manifest.artifacts.filter(artifact => artifact.role === fileRole);
    const paths = await Promise.all([...carried.map(({ entry }) => entry), ...files].map(
      artifact => verifyArtifact(options, root, physicalRoot, artifact)));

    const modules = {};
    for (const [index, { provider }] of carried.entries()) {
      const supplied = provider.modules(await options.importModule(pathToFileURL(paths[index]).href));
      if (supplied === null) {
        throw new TypeError(`deployment Node.js ${provider.name} is incompatible`);
      }
      Object.assign(modules, supplied);
    }
    if (tls !== null) Object.assign(modules, tls);
    return Object.freeze({ modules: Object.freeze(modules) });
  });
}

function readTlsModules(modules) {
  if (modules === null || typeof modules !== "object" || !Object.isFrozen(modules)
      || Object.keys(modules).sort().join() !== tlsModules.join()) {
    throw new TypeError("Node.js wasi:tls must supply exactly its frozen client and types modules");
  }
  return modules;
}

// A file is used from where it lies, so it must lie inside the deployment and hash to the
// manifest's digest.
async function verifyArtifact(options, root, physicalRoot, artifact) {
  const path = resolve(root, ...artifact.relativePath.split("/"));
  const physicalPath = await options.realPath(path);
  if (typeof physicalPath !== "string" || !isAbsolute(physicalPath)
      || isOutside(physicalRoot, physicalPath)) {
    throw new TypeError(`deployment artifact '${artifact.relativePath}' escapes its root`);
  }
  const digest = await options.hashBytes(await readBytes(options, physicalPath));
  if (digest !== artifact.sha256) {
    throw new TypeError(`deployment artifact '${artifact.relativePath}' failed integrity validation`);
  }
  return physicalPath;
}

async function readBytes(options, path) {
  const bytes = await options.readFile(path);
  if (!(bytes instanceof Uint8Array)) {
    throw new TypeError("local Node.js network loader read invalid bytes");
  }
  return bytes;
}

function isOutside(root, path) {
  const value = relative(root, path);
  return value === "" || value === ".." || value.startsWith(`..${sep}`) || isAbsolute(value);
}

function assertExactDataObject(value, keys, label) {
  if (value === null || typeof value !== "object" || Array.isArray(value)
      || Object.getOwnPropertySymbols(value).length !== 0) {
    throw new TypeError(`${label} is invalid`);
  }
  const prototype = Object.getPrototypeOf(value);
  const descriptors = Object.getOwnPropertyDescriptors(value);
  const actualKeys = Object.keys(descriptors).sort();
  if (prototype !== null && prototype !== Object.prototype
      || actualKeys.length !== keys.length
      || actualKeys.some((key, index) => key !== keys[index])
      || Object.values(descriptors).some(
        descriptor => !descriptor.enumerable || !("value" in descriptor))) {
    throw new TypeError(`${label} shape is invalid`);
  }
}
