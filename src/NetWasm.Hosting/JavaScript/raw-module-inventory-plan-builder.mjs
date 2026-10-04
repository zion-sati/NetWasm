const targets = Object.freeze({
  wasm32: Object.freeze({ prefix: "cm32p2", zero: 0 }),
  wasm64: Object.freeze({ prefix: "cm64p2", zero: 0n }),
});
const abiKeys = ["entryPoint", "exports", "imports", "target"];
const importKeys = ["kind", "module", "name"];
const exportKeys = ["kind", "name"];
const descriptorKinds = new Set(["function", "global", "memory", "table", "tag"]);

export function buildRawModuleInventoryPlan({ abi }) {
  assertExactObject(abi, abiKeys, "raw ABI");
  if (abi.target !== "wasm32" && abi.target !== "wasm64") {
    throw new TypeError("raw ABI target is unsupported");
  }
  const target = targets[abi.target];
  const imports = validateDescriptors(abi.imports, importKeys, "raw import", true);
  const exports = validateDescriptors(abi.exports, exportKeys, "raw export", false);
  validateTargetImports(imports, target.prefix);
  return Object.freeze({
    target: abi.target,
    prefix: target.prefix,
    zero: target.zero,
    imports,
    exports,
    reactorHostModule: `${target.prefix}|netwasm:runtime/reactor-host@1`,
    reactorGuestExport: `${target.prefix}|netwasm:runtime/reactor-guest@1|wake`,
  });
}

function validateDescriptors(value, keys, label, hasModule) {
  if (!Array.isArray(value)) {
    throw new TypeError(`${label} descriptors are required`);
  }
  const seen = new Map();
  const descriptors = value.map(descriptor => {
    assertExactObject(descriptor, keys, `${label} descriptor`);
    if (hasModule && (typeof descriptor.module !== "string" || descriptor.module.length === 0)) {
      throw new TypeError(`${label} module is invalid`);
    }
    if (typeof descriptor.name !== "string" || descriptor.name.length === 0
        || !descriptorKinds.has(descriptor.kind)) {
      throw new TypeError(`${label} descriptor is invalid`);
    }
    const identity = hasModule ? descriptor.module : "";
    let names = seen.get(identity);
    if (names === undefined) {
      names = new Set();
      seen.set(identity, names);
    }
    if (names.has(descriptor.name)) {
      throw new TypeError(`${label} descriptor is duplicated`);
    }
    names.add(descriptor.name);
    return Object.freeze(hasModule
      ? { module: descriptor.module, name: descriptor.name, kind: descriptor.kind }
      : { name: descriptor.name, kind: descriptor.kind });
  });
  return Object.freeze(descriptors);
}

function validateTargetImports(imports, expectedPrefix) {
  for (const descriptor of imports) {
    const module = descriptor.module;
    if (module === "wasi_snapshot_preview1" || module === "wasi_unstable"
        || module.endsWith("|wasi_snapshot_preview1")
        || module.endsWith("|wasi_unstable")) {
      throw new TypeError("WASI Preview 1 raw imports are unsupported");
    }
    if ((module.startsWith("cm32p2|") || module.startsWith("cm64p2|"))
        && !module.startsWith(`${expectedPrefix}|`)) {
      throw new TypeError("raw import target does not match the ABI target");
    }
  }
}

function assertExactObject(value, keys, label) {
  if (value === null || typeof value !== "object" || Array.isArray(value)
      || Object.getOwnPropertySymbols(value).length !== 0) {
    throw new TypeError(`${label} is invalid`);
  }
  const descriptors = Object.getOwnPropertyDescriptors(value);
  const actualKeys = Object.keys(descriptors).sort();
  if (actualKeys.length !== keys.length
      || actualKeys.some((key, index) => key !== keys[index])
      || Object.values(descriptors).some(descriptor => !descriptor.enumerable || !("value" in descriptor))) {
    throw new TypeError(`${label} shape is invalid`);
  }
}
