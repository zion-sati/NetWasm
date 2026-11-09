import { dirname, isAbsolute, relative, resolve, sep } from "node:path";
import { pathToFileURL } from "node:url";

import { validateDeploymentManifest } from "./deployment-manifest-validator.mjs";
import { parseStrictJson } from "./strict-json-reader.mjs";

const factoryKeys = ["hashBytes", "importModule", "readFile", "realPath"];
const entryRole = "node-http-provider";
const fileRole = "node-http-provider-file";
const decoder = new TextDecoder("utf-8", { fatal: true });

// Loads the Node.js wasi:http client a deployment that imports wasi:http carries (the SDK
// copies preview3-shim's into it) after verifying each of its files against the deployment
// manifest, and returns it as the network modules NetWasm's WASI platform supplies. A
// deployment without one gets no network.
export function createLocalNodeHttpLoader(options) {
  assertExactDataObject(options, factoryKeys, "local Node.js HTTP loader options");
  for (const [name, value] of Object.entries(options)) {
    if (typeof value !== "function") {
      throw new TypeError(`local Node.js HTTP loader '${name}' action is required`);
    }
  }

  return Object.freeze(async function loadNodeHttp(manifestPath) {
    if (typeof manifestPath !== "string" || manifestPath.includes("\0")
        || !isAbsolute(manifestPath) || resolve(manifestPath) !== manifestPath) {
      throw new TypeError("local deployment manifest path must be canonical and absolute");
    }
    const manifest = validateDeploymentManifest(
      parseStrictJson(decoder.decode(await readBytes(options, manifestPath))));
    const entries = manifest.artifacts.filter(artifact => artifact.role === entryRole);
    if (entries.length === 0) return null;
    if (entries.length !== 1 || entries[0].mediaType !== "text/javascript") {
      throw new TypeError("deployment Node.js HTTP client entry point is invalid");
    }

    const root = dirname(manifestPath);
    const physicalRoot = await options.realPath(root);
    if (typeof physicalRoot !== "string" || !isAbsolute(physicalRoot)) {
      throw new TypeError("deployment physical root is invalid");
    }
    const files = manifest.artifacts.filter(artifact => artifact.role === fileRole);
    const [entryPath] = await Promise.all([entries[0], ...files].map(
      artifact => verifyArtifact(options, root, physicalRoot, artifact)));

    const http = await options.importModule(pathToFileURL(entryPath).href);
    if (http === null || typeof http !== "object"
        || http.types === null || typeof http.types !== "object"
        || http.client === null || typeof http.client !== "object"
        || typeof http.client.send !== "function") {
      throw new TypeError("deployment Node.js HTTP client is incompatible");
    }
    return Object.freeze({
      modules: Object.freeze({
        "wasi:http/types@0.3.2": Object.freeze({ ...http.types }),
        "wasi:http/client@0.3.2": Object.freeze({ send: http.client.send.bind(http.client) }),
      }),
    });
  });
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
    throw new TypeError("local Node.js HTTP loader read invalid bytes");
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
