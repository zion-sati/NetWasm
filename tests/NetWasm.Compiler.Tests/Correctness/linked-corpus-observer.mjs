export function createLinkedCorpusObserver({ instantiate, readTrace }) {
  if (typeof instantiate !== "function" || typeof readTrace !== "function")
    throw new TypeError("Linked observation capabilities are required");
  return Object.freeze({
    async observe(request) {
      if (!request || !Number.isInteger(request.input)
          || request.input < -2147483648 || request.input > 2147483647)
        throw new TypeError("A linked corpus input must be an i32");
      if (typeof request.exposesLegacyTrace !== "boolean" || typeof request.usesTypedTrace !== "boolean")
        throw new TypeError("Trace declarations must be explicit booleans");
      const events = [];
      const managed = await instantiate(event => events.push(event));
      let primaryFailure;
      let observation;
      try {
        if (typeof managed?.dispose !== "function"
            || typeof managed.instance?.exports?.run !== "function")
          throw new TypeError("A disposable linked instance with a run export is required");
        if (events.length !== 0)
          throw new TypeError("Linked initialization reported a terminal event");
        let value = null;
        let exceptionTypeId = null;
        let kind = "value";
        let failure;
        try {
          value = managed.instance.exports.run(request.input);
        } catch (error) {
          failure = error;
          if (!(error instanceof WebAssembly.RuntimeError)
              && !(error instanceof WebAssembly.Exception))
            throw error;
          kind = "trap";
        }
        if (events.length > 1)
          throw new TypeError("A corpus invocation reported multiple terminal events");
        if (events.length === 1) {
          const typeId = events[0]?.typeId;
          if (!Number.isInteger(typeId) || typeId <= 0 || typeId > 2147483647)
            throw new TypeError("A terminal event requires a positive i32 type identity");
          if (!(failure instanceof WebAssembly.RuntimeError))
            throw new TypeError("A terminal event must accompany the terminal Wasm trap");
          kind = "exception";
          exceptionTypeId = typeId;
        }
        if (kind === "value"
            && (!Number.isInteger(value) || value < -2147483648 || value > 2147483647))
          throw new TypeError("A linked corpus result must be an i32");
        observation = {
          kind, value, exceptionTypeId,
          ...readTrace({
            exports: managed.instance.exports,
            exposesLegacyTrace: request.exposesLegacyTrace,
            usesTypedTrace: request.usesTypedTrace,
          }),
        };
      } catch (error) {
        primaryFailure = { error };
        throw error;
      } finally {
        if (typeof managed?.dispose === "function") {
          try {
            managed.dispose();
          } catch (cleanupFailure) {
            if (primaryFailure)
              throw new AggregateError([primaryFailure.error, cleanupFailure],
                "Linked observation and instance disposal both failed");
            throw cleanupFailure;
          }
        }
      }
      return observation;
    },
  });
}
