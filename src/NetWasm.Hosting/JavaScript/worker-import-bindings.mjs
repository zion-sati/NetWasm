// Provider modules may opt into one-way worker notifications without changing their guest interface.
export function bindWorkerImports(namespace, notify) {
  if (!Object.hasOwn(namespace, "createWorkerImports")) return namespace;
  if (typeof namespace.createWorkerImports !== "function" || typeof notify !== "function") {
    throw new TypeError("worker import factory requires a notification action");
  }
  const imports = namespace.createWorkerImports(notify);
  if (imports === null || typeof imports !== "object" || Array.isArray(imports)
      || Object.getPrototypeOf(imports) !== null && Object.getPrototypeOf(imports) !== Object.prototype
      || Object.getOwnPropertySymbols(imports).length !== 0) {
    throw new TypeError("worker import factory must return a function namespace");
  }
  const descriptors = Object.getOwnPropertyDescriptors(imports);
  if (Object.entries(descriptors).some(([name, value]) => name === "createWorkerImports"
      || !value.enumerable || !("value" in value) || typeof value.value !== "function")) {
    throw new TypeError("worker import factory must return a function namespace");
  }
  return Object.freeze(imports);
}
