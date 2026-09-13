import { validateOutputSink } from "./output-sink-stream.mjs";

const encoder = new TextEncoder();

export function createManagedExceptionOutput(stderr) {
  validateOutputSink(stderr, "managed exception stderr output sink");
  return Object.freeze({
    reportImmediate(event) {
      stderr.write(encoder.encode(
        `Managed exception #${event.typeId}: ${event.message ?? "<no stored message>"}\n`));
    },
    reportEnriched(event) {
      stderr.write(encoder.encode(
        `Managed exception #${event.typeId} type: ${event.typeName}\n`));
    },
  });
}
