// Match the resource-drop symbol used by the pinned jco bindings and shim.
// This selects a key; it does not install or mutate a global Symbol property.
export function selectResourceDisposeSymbol(symbols = Symbol) {
  return symbols.dispose ?? symbols.for("dispose");
}

export const resourceDisposeSymbol = selectResourceDisposeSymbol();
