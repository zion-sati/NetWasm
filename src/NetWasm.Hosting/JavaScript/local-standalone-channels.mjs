import { basename, dirname, isAbsolute, join, resolve } from "node:path";

import { validateDeploymentManifest } from "./deployment-manifest-validator.mjs";
import { guestPath } from "./local-directory-grants.mjs";
import { parseStrictJson } from "./strict-json-reader.mjs";

const factoryKeys = ["hashBytes", "readFile"];
const invocationKeys = ["hostExecutablePath", "launcherPath", "preopens"];
const directoryOption = "--dir";
const manifestName = "deployment.json";
const decoder = new TextDecoder("utf-8", { fatal: true });

// A plain `node netwasm.host.mjs [--dir HOST[::GUEST]]... [--] [arguments]` run of a deployment
// folder: the descriptor and request dotnet run would write, made from the deployment.json
// beside the launcher with dotnet run's defaults. The program is named after its application,
// the network, both clocks and random numbers are granted, and only the directories named with
// --dir are mounted, read-write, as `wasmtime run --dir` does. The launcher still appends the
// arguments and overlays the process environment.
export function createStandaloneChannels(options) {
  assertExactDataObject(options, factoryKeys, "standalone channel options");
  for (const [name, value] of Object.entries(options)) {
    if (typeof value !== "function") {
      throw new TypeError(`standalone channel '${name}' action is required`);
    }
  }

  return Object.freeze(async function readStandaloneChannels(invocation) {
    assertExactDataObject(invocation, invocationKeys, "standalone channel invocation");
    if (!Array.isArray(invocation.preopens)) {
      throw new TypeError("standalone preopens must be an array");
    }
    validatePath(invocation.hostExecutablePath, "standalone host executable path");
    validatePath(invocation.launcherPath, "standalone launcher path");
    const manifestPath = join(dirname(invocation.launcherPath), manifestName);
    const bytes = await options.readFile(manifestPath);
    if (!(bytes instanceof Uint8Array)) {
      throw new TypeError("standalone deployment manifest reader returned invalid bytes");
    }
    const manifest = validateDeploymentManifest(parseStrictJson(decoder.decode(bytes)));
    const deploymentManifestSha256 = await options.hashBytes(bytes);
    const application = basename(
      manifest.artifacts.find(artifact => artifact.role === "application").relativePath);
    const descriptor = {
      schemaVersion: 1,
      buildFingerprint: manifest.buildFingerprint,
      deploymentManifestPath: manifestPath,
      deploymentManifestSha256,
      hostingVersion: manifest.versions.hosting,
      hostExecutablePath: invocation.hostExecutablePath,
      launcherPath: invocation.launcherPath,
    };
    const request = {
      schemaVersion: 1,
      buildFingerprint: manifest.buildFingerprint,
      deploymentManifestSha256,
      arguments: [application.endsWith(".wasm") ? application.slice(0, -5) : application],
      environment: [],
      grants: {
        environment: [],
        preopens: invocation.preopens,
        network: "allowAll",
        clocks: ["wall", "monotonic"],
        randomness: true,
        temporaryDirectory: false,
        workingDirectory: false,
      },
      applicationImports: [],
    };
    return Object.freeze({
      descriptorText: JSON.stringify(descriptor),
      requestText: JSON.stringify(request),
    });
  });
}

// Splits a plain launch's leading --dir options from the program's arguments; `--` ends the
// options, so a program can take an argument that looks like one. A directory without a guest
// path is seen at its own absolute path, relative to the working directory.
export function readStandaloneArguments(values, workingDirectory, platform) {
  if (!Array.isArray(values) || typeof workingDirectory !== "string" || typeof platform !== "string") {
    throw new TypeError("standalone arguments, working directory and platform are required");
  }
  const preopens = [];
  const guestPaths = new Set();
  let index = 0;
  while (values[index] === directoryOption) {
    const value = values[index + 1];
    if (typeof value !== "string" || value.length === 0 || value.includes("\0")) {
      throw new TypeError("--dir takes HOST or HOST::GUEST");
    }
    const separator = value.indexOf("::");
    const hostPath = resolve(workingDirectory, separator < 0 ? value : value.slice(0, separator));
    const guest = separator < 0 ? guestPath(hostPath, platform) : value.slice(separator + 2);
    if (!guest.startsWith("/") || guestPaths.has(guest)) {
      throw new TypeError(`--dir guest path '${guest}' must be absolute and unique`);
    }
    guestPaths.add(guest);
    preopens.push({ hostPath, guestPath: guest, access: "readWrite" });
    index += 2;
  }
  if (values[index] === "--") index++;
  return Object.freeze({ arguments: Object.freeze(values.slice(index)), preopens: Object.freeze(preopens) });
}

function validatePath(value, label) {
  if (typeof value !== "string" || value.includes("\0")
      || !isAbsolute(value) || resolve(value) !== value) {
    throw new TypeError(`${label} must be canonical and absolute`);
  }
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
