export function readCorpusTrace({ exports: wasmExports, exposesLegacyTrace, usesTypedTrace }) {
  if (typeof exposesLegacyTrace !== "boolean" || typeof usesTypedTrace !== "boolean")
    throw new TypeError("Trace declarations must be explicit booleans");
  const invoke = (name, ...args) => {
    if (typeof wasmExports?.[name] !== "function")
      throw new TypeError("A declared corpus trace export is missing");
    const value = wasmExports[name](...args);
    if (!Number.isInteger(value) || value < -2147483648 || value > 2147483647)
      throw new TypeError("A corpus trace export did not return an i32");
    return value;
  };
  const trace = exposesLegacyTrace ? invoke("trace") : 0;
  const traceRecords = [];
  if (usesTypedTrace) {
    const count = invoke("trace_count");
    if (count < 0) throw new RangeError("A corpus trace count cannot be negative");
    for (let index = 0; index < count; index++) {
      traceRecords.push({
        kind: invoke("trace_kind", index),
        eventId: invoke("trace_event_id", index),
        payloadLow: invoke("trace_payload_low", index),
        payloadHigh: invoke("trace_payload_high", index),
      });
    }
  }
  return { trace, traceRecords };
}
