import runtimePolicy from '../runtime/runtime-policy.json' with { type: 'json' };

export function validateRuntimeImports(imports, target) {
  if (target !== 'wasm32' && target !== 'wasm64') {
    throw new Error('Unsupported runtime target');
  }
  const prefix = target === 'wasm64' ? 'cm64p2' : 'cm32p2';
  const contracts = runtimePolicy.staticNative.imports.map(entry => ({
    ...entry, module: entry.module.replace('{prefix}', prefix),
  }));
  const allowedImports = new Map();
  const requiredImports = new Map();
  for (const entry of contracts) {
    const names = allowedImports.get(entry.module) ?? new Set();
    names.add(entry.name);
    allowedImports.set(entry.module, names);
    if (entry.required) {
      const required = requiredImports.get(entry.module) ?? new Set();
      required.add(entry.name);
      requiredImports.set(entry.module, required);
    }
  }
  const observed = new Map();
  for (const entry of imports) {
    if (entry.kind !== 'function' || !allowedImports.get(entry.module)?.has(entry.name)) {
      throw new Error(`Unexpected runtime import: ${entry.module}.${entry.name} (${entry.kind})`);
    }
    const names = observed.get(entry.module) ?? new Set();
    if (names.has(entry.name)) throw new Error('Duplicate runtime import');
    names.add(entry.name);
    observed.set(entry.module, names);
  }
  for (const [module, names] of requiredImports) {
    for (const name of names) {
      if (!observed.get(module)?.has(name)) {
        throw new Error(`Missing runtime import: ${module}.${name}`);
      }
    }
  }
}
