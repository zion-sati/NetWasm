import { isAbsolute } from "node:path";

import { failedExecutionResult } from "./execution-result.mjs";
import { validateOutputSink } from "./output-sink-stream.mjs";

const factoryKeys = [
  "appendArguments",
  "createExecution",
  "loadPlatform",
  "readDescriptor",
  "readRequest",
  "realPath",
  "verifyPackage",
];
const invocationKeys = [
  "arguments",
  "descriptorText",
  "hostExecutablePath",
  "launcherPath",
  "requestText",
  "signal",
  "stderr",
  "stdout",
];
const toolchainId = "NetWasm.Toolchain";

export function createLocalLauncher(options) {
  assertExactDataObject(options, factoryKeys, "local launcher options");
  for (const [name, value] of Object.entries(options)) {
    if (typeof value !== "function") {
      throw new TypeError(`local launcher '${name}' action is required`);
    }
  }

  return Object.freeze(async function launchLocalNetWasm(invocation) {
    assertExactDataObject(invocation, invocationKeys, "local launcher invocation");
    validateText(invocation.descriptorText, "local launcher descriptor text");
    validateText(invocation.requestText, "local launcher request text");
    validatePath(invocation.hostExecutablePath, "local launcher host executable path");
    validatePath(invocation.launcherPath, "local launcher module path");
    validateSignal(invocation.signal);
    validateOutputSink(invocation.stdout, "local launcher stdout output sink");
    validateOutputSink(invocation.stderr, "local launcher stderr output sink");
    if (invocation.signal?.aborted) return callerCancellation();

    let descriptor;
    try {
      descriptor = options.readDescriptor(invocation.descriptorText);
    } catch {
      return hostFailure("host.descriptor", "The execution descriptor is invalid.");
    }

    let request;
    try {
      request = options.appendArguments(
        options.readRequest(invocation.requestText),
        invocation.arguments);
      if (request.buildFingerprint !== descriptor.buildFingerprint
          || request.deploymentManifestSha256 !== descriptor.deploymentManifestSha256) {
        return contractFailure();
      }
    } catch {
      return contractFailure();
    }
    if (invocation.signal?.aborted) return callerCancellation();

    try {
      const [actualHost, expectedHost, actualLauncher, expectedLauncher] = await Promise.all([
        options.realPath(invocation.hostExecutablePath),
        options.realPath(descriptor.hostExecutablePath),
        options.realPath(invocation.launcherPath),
        options.realPath(descriptor.launcherPath),
      ]);
      if (actualHost !== expectedHost || actualLauncher !== expectedLauncher) {
        return hostFailure(
          "host.launcher-identity",
          "The execution launcher does not match its descriptor.");
      }
    } catch {
      return hostFailure(
        "host.launcher-identity",
        "The execution launcher identity could not be verified.");
    }
    if (invocation.signal?.aborted) return callerCancellation();

    let toolchain;
    let platform;
    try {
      const packages = new Map(descriptor.toolPackages.map(
        package_ => [package_.id, package_]));
      toolchain = packages.get(toolchainId);
      if (toolchain === undefined) {
        return hostFailure(
          "host.toolchain",
          "The execution descriptor does not select NetWasm.Toolchain.");
      }
      await options.verifyPackage(toolchain);
      platform = await options.loadPlatform(toolchain);
    } catch {
      return hostFailure(
        "host.toolchain",
        "The selected NetWasm.Toolchain package could not be verified.");
    }
    if (invocation.signal?.aborted) return callerCancellation();

    let execute;
    try {
      execute = options.createExecution(Object.freeze({
        manifestPath: descriptor.deploymentManifestPath,
        platform,
      }));
      if (typeof execute !== "function") {
        throw new TypeError("local execution factory returned no action");
      }
    } catch {
      return hostFailure(
        "host.composition",
        "The local execution host could not be composed.");
    }

    try {
      return await execute(Object.freeze({
        request,
        signal: invocation.signal,
        stderr: invocation.stderr,
        stdout: invocation.stdout,
      }));
    } catch {
      return hostFailure("host.execution", "The local execution host failed.", "execution");
    }
  });
}

function contractFailure() {
  return failedExecutionResult(
    "contractFailure",
    "validation",
    "host.contract",
    "The execution request does not match the selected deployment.");
}

function callerCancellation() {
  return failedExecutionResult(
    "callerCancellation",
    "validation",
    "host.cancelled",
    "Execution was cancelled by the caller.");
}

function hostFailure(code, message, phase = "validation") {
  return failedExecutionResult("hostFailure", phase, code, message);
}

function validateText(value, label) {
  if (typeof value !== "string" || value.length === 0) {
    throw new TypeError(`${label} is required`);
  }
}

function validatePath(value, label) {
  if (typeof value !== "string" || value.length === 0
      || value.includes("\0") || !isAbsolute(value)) {
    throw new TypeError(`${label} must be an absolute local path without NUL`);
  }
}

function validateSignal(signal) {
  if (signal !== null && (typeof signal !== "object"
      || typeof signal.aborted !== "boolean"
      || typeof signal.addEventListener !== "function"
      || typeof signal.removeEventListener !== "function")) {
    throw new TypeError("local launcher signal must be an AbortSignal");
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
