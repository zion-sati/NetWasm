import { createLinkedCorpusObserver } from "./linked-corpus-observer.mjs";
import { readCorpusTrace } from "./corpus-trace-reader.mjs";

export function createLinkedCorpusProgram({
  readBinary,
  readText,
  compileModule,
  listImports,
  hash,
  instantiate,
  createRuntimeImports,
}) {
  for (const capability of [readBinary, readText, compileModule, listImports,
    hash, instantiate, createRuntimeImports]) {
    if (typeof capability !== "function")
      throw new TypeError("Linked corpus program capabilities are required");
  }

  return Object.freeze({
    async run(request) {
      validateRequest(request);
      const [moduleBytes, manifestText] = await Promise.all([
        readBinary(request.modulePath),
        readText(request.manifestPath),
      ]);
      const moduleSha256 = hash(moduleBytes);
      const manifestSha256 = hash(new TextEncoder().encode(manifestText));
      if (moduleSha256 !== request.moduleSha256
          || manifestSha256 !== request.manifestSha256)
        throw new TypeError("Linked corpus artifact identity changed before execution");
      const manifest = JSON.parse(manifestText);
      if (manifest?.target !== request.target)
        throw new TypeError("Linked corpus manifest target differs from its request");
      const module = await compileModule(moduleBytes);
      if (listImports(module).some(({ module: name }) =>
        name === "wasi_snapshot_preview1" || name === "wasi_unstable"))
        throw new TypeError("Linked corpus module imports forbidden Preview 1 services");

      const observer = createLinkedCorpusObserver({
        async instantiate(reportImmediate) {
          let managed;
          const prefix = request.target === "wasm64" ? "cm64p2" : "cm32p2";
          managed = await instantiate({
            module,
            manifest,
            runtimeModules: createRuntimeImports(
              request.target, () => managed.instance.exports.memory,
              token => managed.instance.exports[`${prefix}|netwasm:runtime/reactor-guest@1|wake`](token)),
            managedExceptionReporting: { reportImmediate },
          });
          return managed;
        },
        readTrace: readCorpusTrace,
      });
      const observations = [];
      for (const input of request.inputs) {
        observations.push({ input, ...await observer.observe({
          input,
          exposesLegacyTrace: request.exposesLegacyTrace,
          usesTypedTrace: request.usesTypedTrace,
        }) });
      }
      return {
        schemaVersion: 1,
        target: request.target,
        moduleSha256,
        manifestSha256,
        observations,
      };
    },
  });
}

function validateRequest(request) {
  if (!request || request.schemaVersion !== 1
      || (request.target !== "wasm32" && request.target !== "wasm64")
      || typeof request.modulePath !== "string" || request.modulePath.length === 0
      || typeof request.manifestPath !== "string" || request.manifestPath.length === 0
      || !/^[0-9a-f]{64}$/.test(request.moduleSha256)
      || !/^[0-9a-f]{64}$/.test(request.manifestSha256)
      || typeof request.exposesLegacyTrace !== "boolean"
      || typeof request.usesTypedTrace !== "boolean"
      || !Array.isArray(request.inputs) || request.inputs.length === 0)
    throw new TypeError("Invalid linked corpus request");
  const unique = new Set();
  for (const input of request.inputs) {
    if (!Number.isInteger(input) || input < -2147483648 || input > 2147483647
        || unique.has(input))
      throw new TypeError("Linked corpus inputs must be unique i32 values");
    unique.add(input);
  }
}
