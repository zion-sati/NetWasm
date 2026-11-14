// Test-runtime imports must be declared explicitly. This adapter does not turn
// a simulation into a production-runtime capability; it prevents missing imports
// from silently acquiring a function that returns zero.
//
// The oracle links no native library, so a program that reaches one (NetWasm.App.Runtime's
// cryptography shim, say) imports its entry points. Names the optional unlinkedNativeEntryPoint
// predicate accepts resolve to a function that throws when called: a program may link the native
// code without calling it, and a call fails loudly instead of returning zero.
export function createDeclaredOracleImports(moduleName, values, unlinkedNativeEntryPoint = () => false) {
  if (typeof moduleName !== "string" || moduleName.length === 0) {
    throw new TypeError("An oracle import module name is required.");
  }
  if (values === null || typeof values !== "object") {
    throw new TypeError("An oracle import declaration object is required.");
  }
  if (typeof unlinkedNativeEntryPoint !== "function") {
    throw new TypeError("The unlinked native entry point predicate must be a function.");
  }
  return new Proxy(values, {
    get(target, name) {
      if (!Object.hasOwn(target, name)) {
        if (typeof name === "string" && unlinkedNativeEntryPoint(name)) {
          return () => {
            throw new Error(`Native entry point not linked by the oracle: ${moduleName}.${name}`);
          };
        }
        throw new Error(`Undeclared oracle import: ${moduleName}.${String(name)}`);
      }
      return Reflect.get(target, name);
    },
  });
}
