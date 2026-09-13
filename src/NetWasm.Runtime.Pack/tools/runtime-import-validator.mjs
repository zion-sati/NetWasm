export function validateRuntimeImports(imports, target) {
  if (target !== 'wasm32' && target !== 'wasm64') {
    throw new Error('Unsupported runtime target');
  }
  const prefix = target === 'wasm64' ? 'cm64p2' : 'cm32p2';
  const requiredImports = new Map([
    ['netwasm.application.v1', new Set(['netwasm.filter', 'netwasm.finalize'])],
    [`${prefix}|wasi:cli/environment@0.2`, new Set(['get-environment'])],
  ]);
  const allowedImports = new Map([
    ...requiredImports,
    [`${prefix}|wasi:cli/stdout@0.2`, new Set(['get-stdout'])],
    [`${prefix}|wasi:cli/stderr@0.2`, new Set(['get-stderr'])],
    [`${prefix}|wasi:io/streams@0.2`, new Set([
      '[method]output-stream.blocking-write-and-flush', 'output-stream_drop',
    ])],
    [`${prefix}|wasi:io/error@0.2`, new Set(['error_drop'])],
    [`${prefix}|wasi:cli/exit@0.2`, new Set(['exit'])],
  ]);
  const observed = new Map();
  for (const entry of imports) {
    if (entry.kind !== 'function' || !allowedImports.get(entry.module)?.has(entry.name)) {
      throw new Error(`Unexpected runtime import: ${entry.module}.${entry.name} (${entry.kind})`);
    }
    const names = observed.get(entry.module) ?? new Set();
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
