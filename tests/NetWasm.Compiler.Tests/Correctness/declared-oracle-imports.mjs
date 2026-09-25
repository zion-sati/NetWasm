// Test-runtime imports must be declared explicitly. This adapter does not turn
// a simulation into a production-runtime capability; it prevents missing imports
// from silently acquiring a function that returns zero.
export function createDeclaredOracleImports(moduleName, values) {
  if (typeof moduleName !== "string" || moduleName.length === 0) {
    throw new TypeError("An oracle import module name is required.");
  }
  if (values === null || typeof values !== "object") {
    throw new TypeError("An oracle import declaration object is required.");
  }
  return new Proxy(values, {
    get(target, name) {
      if (!Object.hasOwn(target, name)) {
        throw new Error(`Undeclared oracle import: ${moduleName}.${String(name)}`);
      }
      return Reflect.get(target, name);
    },
  });
}
