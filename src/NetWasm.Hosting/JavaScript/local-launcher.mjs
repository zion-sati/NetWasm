import { isAbsolute } from "node:path";

import { failedExecutionResult } from "./execution-result.mjs";
import { validateInputSource } from "./input-source.mjs";
import { validateOutputSink } from "./output-sink-stream.mjs";

const factoryKeys = [
  "appendArguments",
  "createExecution",
  "grantDirectories",
  "loadPlatform",
  "overlayEnvironment",
  "readDescriptor",
  "readRequest",
  "realPath",
];
const invocationKeys = [
  "arguments",
  "descriptorText",
  "environment",
  "hostExecutablePath",
  "launcherPath",
  "requestText",
  "signal",
  "stderr",
  "stdin",
  "stdout",
];

export function createLocalLauncher(options) {
  assertExactDataObject(options, factoryKeys, "local launcher options");
  for (const [name, value] of Object.entries(options)) {
    if (typeof value !== "function") {
      throw new TypeError(`local launcher '${name}' action is required`);
    }
  }

  return Object.freeze(async function launchLocalNetWasm(invocation) {
    return launch(options, invocation);
  });
}

async function launch(options, invocation) {
  assertExactDataObject(invocation, invocationKeys, "local launcher invocation");
  validateText(invocation.descriptorText, "local launcher descriptor text");
  validateText(invocation.requestText, "local launcher request text");
  if (!Array.isArray(invocation.environment)) {
    throw new TypeError("local launcher environment must be an array");
  }
  validatePath(invocation.hostExecutablePath, "local launcher host executable path");
  validatePath(invocation.launcherPath, "local launcher module path");
  validateSignal(invocation.signal);
  validateOutputSink(invocation.stdout, "local launcher stdout output sink");
  validateOutputSink(invocation.stderr, "local launcher stderr output sink");
  validateInputSource(invocation.stdin, "local launcher stdin input source");
  if (invocation.signal?.aborted) return callerCancellation();

  let descriptor;
  try {
    descriptor = options.readDescriptor(invocation.descriptorText);
  } catch (error) {
    return hostFailure("host.descriptor", "The execution descriptor is invalid.", error);
  }

  let request;
  try {
    request = options.overlayEnvironment(
      options.appendArguments(
        options.readRequest(invocation.requestText),
        invocation.arguments),
      invocation.environment);
    if (request.buildFingerprint !== descriptor.buildFingerprint
        || request.deploymentManifestSha256 !== descriptor.deploymentManifestSha256) {
      return contractFailure();
    }
  } catch (error) {
    return contractFailure(error);
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
  } catch (error) {
    return hostFailure(
      "host.launcher-identity",
      "The execution launcher identity could not be verified.",
      error);
  }
  if (invocation.signal?.aborted) return callerCancellation();

  let directories;
  try {
    directories = await options.grantDirectories(request);
  } catch (error) {
    return hostFailure(
      "host.directories",
      "The working or temporary directory could not be granted.",
      error);
  }
  try {
    return await executeLaunch(options, invocation, descriptor, directories);
  } finally {
    await directories.release();
  }
}

async function executeLaunch(options, invocation, descriptor, directories) {
  if (invocation.signal?.aborted) return callerCancellation();
  let platform;
  try {
    platform = await options.loadPlatform(Object.freeze({
      initialDirectory: directories.initialDirectory,
      manifestPath: descriptor.deploymentManifestPath,
    }));
  } catch (error) {
    return hostFailure(
      "host.platform",
      "The deployment's host services could not be loaded.",
      error);
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
  } catch (error) {
    return hostFailure(
      "host.composition",
      "The local execution host could not be composed.",
      error);
  }

  try {
    return await execute(Object.freeze({
      request: directories.request,
      signal: invocation.signal,
      stderr: invocation.stderr,
      stdin: invocation.stdin,
      stdout: invocation.stdout,
    }));
  } catch (error) {
    return hostFailure("host.execution", "The local execution host failed.", error, "execution");
  }
}

function contractFailure(cause) {
  return failedExecutionResult(
    "contractFailure",
    "validation",
    "host.contract",
    "The execution request does not match the selected deployment.",
    cause);
}

function callerCancellation() {
  return failedExecutionResult(
    "callerCancellation",
    "validation",
    "host.cancelled",
    "Execution was cancelled by the caller.");
}

function hostFailure(code, message, cause, phase = "validation") {
  return failedExecutionResult("hostFailure", phase, code, message, cause);
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
