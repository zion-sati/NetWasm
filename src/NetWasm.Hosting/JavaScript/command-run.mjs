import {
  consoleLine,
  createLineOutput,
  mergeEnvironment,
  readEnvironmentOverride,
} from "./session-options.mjs";

const optionKeys = new Set(["arguments", "environment", "signal", "stderr", "stdout"]);

// The generated run() helper: one published command deployment, its build's
// arguments, environment and grants, and per-call overrides. Arguments replace
// everything after the program name, as Main sees them; environment values
// merge over the build's; output arrives as lines.
export function createCommandRun(executeNetWasm) {
  if (typeof executeNetWasm !== "function") {
    throw new TypeError("command run requires executeNetWasm");
  }
  return async function runNetWasm({ configuration, options, startup }) {
    const selected = readOptions(options);
    const merged = mergeEnvironment(configuration, selected.environment);
    const stdout = createLineOutput(selected.stdout ?? consoleLine("log"));
    const stderr = createLineOutput(selected.stderr ?? consoleLine("error"));
    try {
      return await executeNetWasm({
        request: {
          schemaVersion: 1,
          buildFingerprint: startup.buildFingerprint,
          deploymentManifestSha256: startup.manifestSha256,
          arguments: selected.arguments === undefined
            ? merged.arguments
            : [...merged.arguments.slice(0, 1), ...selected.arguments],
          environment: merged.environment,
          grants: merged.grants,
          applicationImports: merged.applicationImports,
        },
        stdout: stdout.sink,
        stderr: stderr.sink,
        // A run in a page or worker has no standard input.
        stdin: null,
        signal: selected.signal ?? null,
      });
    } finally {
      stdout.flush();
      stderr.flush();
    }
  };
}

function readOptions(options) {
  if (options === undefined) return {};
  if (options === null || typeof options !== "object" || Array.isArray(options)) {
    throw new TypeError("run options must be an object");
  }
  for (const key of Object.keys(options)) {
    if (!optionKeys.has(key)) throw new TypeError(`unknown run option ${key}`);
  }
  if (options.arguments !== undefined && (!Array.isArray(options.arguments)
      || options.arguments.some(argument => typeof argument !== "string"))) {
    throw new TypeError("run arguments must be an array of strings");
  }
  for (const key of ["stdout", "stderr"]) {
    if (options[key] !== undefined && typeof options[key] !== "function") {
      throw new TypeError(`run ${key} must be a line callback`);
    }
  }
  if (options.signal != null && !(options.signal instanceof AbortSignal)) {
    throw new TypeError("run signal must be an AbortSignal");
  }
  return {
    arguments: options.arguments === undefined ? undefined : Object.freeze([...options.arguments]),
    environment: readEnvironmentOverride(options.environment, "run environment"),
    signal: options.signal,
    stderr: options.stderr,
    stdout: options.stdout,
  };
}
