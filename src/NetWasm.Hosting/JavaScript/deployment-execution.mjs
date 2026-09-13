import { createContractError, isContractError } from "./contract-error.mjs";
import { assertExecutionResult, failedExecutionResult } from "./execution-result.mjs";
import { validateOutputSink } from "./output-sink-stream.mjs";

const factoryKeys = [
  "createFilesystem",
  "createOutputFinalization",
  "createTimeZoneMaterializer",
  "executeWithResources",
  "loadManifest",
  "prepareProviders",
  "prepareRawInterop",
  "resolveStrategy",
  "selectTimeZone",
  "validateRequest",
];
const executionKeys = ["request", "signal", "stderr", "stdout"];
const outputKeys = ["releaseActions", "stderr", "stdout"];
const providerKeys = ["binding", "componentImports", "consumerModules", "rawProviders"];

export function createDeploymentExecution(options) {
  assertExactDataObject(options, factoryKeys, "deployment execution options");
  for (const [name, value] of Object.entries(options)) {
    if (name === "prepareRawInterop" ? value !== null && typeof value !== "function"
      : typeof value !== "function") {
      throw new TypeError(`deployment execution '${name}' action is required`);
    }
  }

  return Object.freeze(async function executeDeployment(input) {
    assertExactDataObject(input, executionKeys, "NetWasm execution");
    validateSignal(input.signal);
    validateOutputSink(input.stdout, "NetWasm stdout output sink");
    validateOutputSink(input.stderr, "NetWasm stderr output sink");
    if (input.signal?.aborted) return callerCancellation("validation");

    let request;
    let manifest;
    let selection;
    let executeStrategy;
    try {
      request = options.validateRequest(input.request);
      manifest = await options.loadManifest(Object.freeze({
        expectedSha256: request.deploymentManifestSha256,
        signal: input.signal,
      }));
      if (request.buildFingerprint !== manifest.buildFingerprint) {
        throw createContractError();
      }
      selection = options.selectTimeZone({
        artifacts: manifest.artifacts,
        environment: request.environment,
        runtimeFeatures: manifest.runtimeFeatures,
      });
      executeStrategy = options.resolveStrategy(manifest.deploymentKind);
      if (typeof executeStrategy !== "function") {
        throw new TypeError("artifact Strategy resolver returned an invalid action");
      }
    } catch (error) {
      if (input.signal?.aborted) return callerCancellation("validation");
      return isContractError(error)
        ? contractFailure()
        : hostFailure("validation", "host.validation",
          "The execution host failed during validation.");
    }

    let output;
    try {
      output = options.createOutputFinalization({ stderr: input.stderr, stdout: input.stdout });
      assertExactDataObject(output, outputKeys, "output finalization");
      validateOutputSink(output.stdout, "supervised stdout output sink");
      validateOutputSink(output.stderr, "supervised stderr output sink");
      if (!Array.isArray(output.releaseActions) || !Object.isFrozen(output.releaseActions)) {
        throw new TypeError("output finalization release actions must be immutable");
      }
    } catch {
      return hostFailure("validation", "host.output-prepare",
        "The execution host could not prepare application output.");
    }

    let outcome;
    try {
      outcome = await options.executeWithResources(Object.freeze({
        selection,
        signal: input.signal,
        releaseActions: output.releaseActions,
        createFilesystem: () => options.createFilesystem(request.grants.preopens),
        createMaterializer: options.createTimeZoneMaterializer,
        execute: async ({ filesystem, signal }) => {
          let providers;
          try {
            providers = await options.prepareProviders(Object.freeze({
              filesystem,
              manifest,
              request,
              signal,
              stderr: output.stderr,
              stdout: output.stdout,
            }));
            assertExactDataObject(providers, providerKeys, "prepared execution providers");
          } catch (error) {
            if (signal?.aborted) return callerCancellation("instantiation");
            return isContractError(error)
              ? contractFailure()
              : hostFailure("instantiation", "host.provider-prepare",
                "The execution host could not prepare application providers.");
          }

          let result;
          if (manifest.deploymentKind === "raw") {
            if (typeof options.prepareRawInterop !== "function") {
              return hostFailure("instantiation", "host.interop-prepare",
                "The execution host could not prepare managed interop.");
            }
            result = await executeStrategy(Object.freeze({
              artifacts: manifest.artifacts,
              contractKey: manifest.executionContract,
              prepareInterop: interop => options.prepareRawInterop(Object.freeze({
                ...interop,
                consumerModules: providers.consumerModules,
                stderr: output.stderr,
              })),
              providers: providers.rawProviders,
              signal,
            }));
          } else {
            result = await executeStrategy(Object.freeze({
              artifacts: manifest.artifacts,
              contractKey: manifest.executionContract,
              imports: providers.componentImports,
              signal,
            }));
          }
          assertExecutionResult(result);
          return result;
        },
      }));
    } catch {
      return input.signal?.aborted
        ? callerCancellation("execution")
        : hostFailure("execution", "host.execution", "The execution host failed.");
    }
    try {
      assertExecutionResult(outcome);
      return outcome;
    } catch {
      return hostFailure("execution", "host.execution", "The execution host failed.");
    }
  });
}

function callerCancellation(phase) {
  return failedExecutionResult(
    "callerCancellation", phase, "caller.cancelled", "Execution was cancelled by the caller.");
}

function contractFailure() {
  return failedExecutionResult(
    "contractFailure", "validation", "contract.invalid", "Execution contract validation failed.");
}

function hostFailure(phase, code, message) {
  return failedExecutionResult("hostFailure", phase, code, message);
}

function validateSignal(signal) {
  if (signal !== null && (typeof signal !== "object"
      || typeof signal.aborted !== "boolean"
      || typeof signal.addEventListener !== "function"
      || typeof signal.removeEventListener !== "function")) {
    throw new TypeError("NetWasm execution signal must be an AbortSignal");
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
