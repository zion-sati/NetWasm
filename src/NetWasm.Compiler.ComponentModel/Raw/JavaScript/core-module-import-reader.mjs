import { RawModuleInspectionError } from "./raw-module-inspection-error.mjs";

export async function readCoreModuleImports(bytes, engine) {
  if (!(bytes instanceof Uint8Array)) {
    throw new RawModuleInspectionError("invalid-bytes");
  }
  if (typeof engine?.compile !== "function"
      || typeof engine.Module?.imports !== "function") {
    throw new TypeError("A core-module inspection engine is required.");
  }
  try {
    const module = await engine.compile(new Uint8Array(bytes));
    return Object.freeze(engine.Module.imports(module).map(entry => Object.freeze({
      module: entry.module,
      name: entry.name,
      kind: entry.kind,
    })));
  } catch (cause) {
    throw new RawModuleInspectionError("invalid-core-module", { cause });
  }
}
