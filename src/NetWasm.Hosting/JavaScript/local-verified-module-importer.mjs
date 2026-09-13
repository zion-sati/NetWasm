import { registerHooks } from "node:module";

let verifiedModuleSequence = 0n;

export async function importLocalVerifiedModule(bytes, artifact) {
  if (!(bytes instanceof Uint8Array)) {
    throw new TypeError("verified JavaScript module bytes are required");
  }
  if (artifact === null || typeof artifact !== "object"
      || typeof artifact.relativePath !== "string" || artifact.relativePath.length === 0) {
    throw new TypeError("verified JavaScript module artifact is required");
  }

  verifiedModuleSequence++;
  const url = `netwasm-verified:${verifiedModuleSequence}/${encodeURIComponent(artifact.relativePath)}`;
  const source = new Uint8Array(bytes);
  const hooks = registerHooks({
    resolve(specifier, context, nextResolve) {
      if (specifier === url) {
        return { url, format: "module", shortCircuit: true };
      }
      if (context.parentURL === url) {
        throw new TypeError("verified JavaScript cannot import dependencies");
      }
      return nextResolve(specifier, context);
    },
    load(candidate, context, nextLoad) {
      if (candidate === url) {
        return { format: "module", source, shortCircuit: true };
      }
      return nextLoad(candidate, context);
    },
  });
  try {
    return await import(url);
  } finally {
    hooks.deregister();
  }
}
