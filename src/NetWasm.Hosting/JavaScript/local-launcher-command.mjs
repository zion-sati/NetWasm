import { isAbsolute, resolve } from "node:path";

import { assertExecutionResult, failedExecutionResult } from "./execution-result.mjs";
import { validateOutputSink } from "./output-sink-stream.mjs";

const factoryKeys = [
  "deleteResult",
  "launch",
  "readFileText",
  "readStdinText",
  "writeResultText",
];
const invocationKeys = [
  "arguments",
  "environment",
  "hostExecutablePath",
  "launcherPath",
  "signal",
  "stderr",
  "stdout",
];
const optionNames = new Set(["--descriptor", "--request", "--result"]);
const replaceResultOption = "--replace-result";

export function createLocalLauncherCommand(options) {
  assertExactDataObject(options, factoryKeys, "local launcher command options");
  for (const [name, value] of Object.entries(options)) {
    if (typeof value !== "function") {
      throw new TypeError(`local launcher command '${name}' action is required`);
    }
  }

  return Object.freeze(async function runLocalLauncherCommand(invocation) {
    assertExactDataObject(invocation, invocationKeys, "local launcher command invocation");
    if (!Array.isArray(invocation.arguments)) {
      throw new TypeError("local launcher command arguments must be an array");
    }
    if (!Array.isArray(invocation.environment)) {
      throw new TypeError("local launcher command environment must be an array");
    }
    validatePath(invocation.hostExecutablePath, "local launcher command host executable path");
    validatePath(invocation.launcherPath, "local launcher command module path");
    validateSignal(invocation.signal);
    validateOutputSink(invocation.stdout, "local launcher command stdout output sink");
    validateOutputSink(invocation.stderr, "local launcher command stderr output sink");
    const paths = readArguments(invocation.arguments);
    if (paths.descriptor === paths.result || paths.request !== "-"
        && (paths.request === paths.descriptor || paths.request === paths.result)) {
      throw new TypeError("local launcher command channels must use distinct paths");
    }
    if (paths.replaceResult) await options.deleteResult(paths.result);

    let result;
    try {
      const [descriptorText, requestText] = await Promise.all([
        options.readFileText(paths.descriptor),
        paths.request === "-"
          ? options.readStdinText()
          : options.readFileText(paths.request),
      ]);
      result = await options.launch(Object.freeze({
        arguments: paths.arguments,
        descriptorText,
        environment: invocation.environment,
        hostExecutablePath: invocation.hostExecutablePath,
        launcherPath: invocation.launcherPath,
        requestText,
        signal: invocation.signal,
        stderr: invocation.stderr,
        stdout: invocation.stdout,
      }));
      assertExecutionResult(result);
    } catch {
      result = failedExecutionResult(
        "hostFailure",
        "validation",
        "host.launcher-input",
        "The local execution launcher could not read or execute its request.");
    }

    await options.writeResultText(paths.result, `${JSON.stringify(result)}\n`);
    return result;
  });
}

function readArguments(values) {
  const argumentSeparator = values.indexOf("--");
  const optionValues = argumentSeparator < 0
    ? values
    : values.slice(0, argumentSeparator);
  const applicationArguments = argumentSeparator < 0
    ? []
    : values.slice(argumentSeparator + 1);
  if (optionValues.length !== 6 && optionValues.length !== 7) {
    throw new TypeError("local launcher command requires descriptor, request, and result options");
  }
  const parsed = Object.create(null);
  let replaceResult = false;
  for (let index = 0; index < optionValues.length;) {
    const name = optionValues[index];
    if (name === replaceResultOption) {
      if (replaceResult) {
        throw new TypeError("local launcher command option is unknown or duplicated");
      }
      replaceResult = true;
      index++;
      continue;
    }
    const value = optionValues[index + 1];
    if (!optionNames.has(name) || Object.hasOwn(parsed, name)) {
      throw new TypeError("local launcher command option is unknown or duplicated");
    }
    if (name === "--request" && value === "-") {
      parsed[name] = value;
    } else {
      validatePath(value, `local launcher command ${name.slice(2)} path`);
      if (resolve(value) !== value) {
        throw new TypeError(`local launcher command ${name.slice(2)} path must be canonical`);
      }
      parsed[name] = value;
    }
    index += 2;
  }
  return Object.freeze({
    arguments: Object.freeze([...applicationArguments]),
    descriptor: parsed["--descriptor"],
    replaceResult,
    request: parsed["--request"],
    result: parsed["--result"],
  });
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
    throw new TypeError("local launcher command signal must be an AbortSignal");
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
