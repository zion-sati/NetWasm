import { RawModuleInspectionError } from "./raw-module-inspection-error.mjs";

export function readBinaryenFunctionImports(bytes, decoder) {
  if (!(bytes instanceof Uint8Array)) {
    throw new RawModuleInspectionError("invalid-bytes");
  }
  if (typeof decoder?.readBinary !== "function"
      || typeof decoder.getFunctionInfo !== "function"
      || typeof decoder.expandType !== "function"
      || typeof decoder.Features?.All !== "number") {
    throw new TypeError("A Binaryen module decoder is required.");
  }
  const valueTypes = new Map([
    [decoder.i32, "i32"], [decoder.i64, "i64"],
    [decoder.f32, "f32"], [decoder.f64, "f64"],
  ]);
  const projectTypes = type => Object.freeze(decoder.expandType(type).map(value => {
    const name = valueTypes.get(value);
    if (name === undefined) {
      throw new RawModuleInspectionError("unsupported-import-signature");
    }
    return name;
  }));

  try {
    const module = decoder.readBinary(new Uint8Array(bytes), decoder.Features.All);
    try {
      const imports = [];
      for (let index = 0; index < module.getNumFunctions(); index++) {
        const info = decoder.getFunctionInfo(module.getFunctionByIndex(index));
        if (info.body !== 0) continue;
        imports.push(Object.freeze({
          module: info.module,
          name: info.base,
          parameters: projectTypes(info.params),
          results: projectTypes(info.results),
        }));
      }
      return Object.freeze(imports);
    } finally {
      module.dispose();
    }
  } catch (cause) {
    if (cause instanceof RawModuleInspectionError) throw cause;
    throw new RawModuleInspectionError("decoder-failure", { cause });
  }
}
