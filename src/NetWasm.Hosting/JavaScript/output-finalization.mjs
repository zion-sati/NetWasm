import { validateOutputSink } from "./output-sink-stream.mjs";

const requestKeys = ["stderr", "stdout"];

export function createOutputFinalization(request) {
  assertExactDataObject(request, requestKeys, "output finalization request");
  validateOutputSink(request.stdout, "stdout output sink");
  validateOutputSink(request.stderr, "stderr output sink");
  const stdout = supervise(request.stdout);
  const stderr = supervise(request.stderr);
  return Object.freeze({
    stdout: stdout.sink,
    stderr: stderr.sink,
    releaseActions: Object.freeze([
      releaseAction("stdout", stdout.failed),
      releaseAction("stderr", stderr.failed),
    ]),
  });
}

function supervise(sink) {
  let failure = false;
  return Object.freeze({
    sink: Object.freeze({
      write(bytes) {
        if (failure) return;
        try {
          sink.write(bytes);
        } catch {
          failure = true;
        }
      },
    }),
    failed: () => failure,
  });
}

function releaseAction(name, failed) {
  return Object.freeze({
    phase: "output",
    code: `host.${name}-output`,
    message: `The execution host could not deliver application ${name}.`,
    release() {
      if (failed()) throw new Error("output delivery failed");
    },
  });
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
