export function createRefreshableDataView(memory) {
  return new Proxy(Object.create(null), {
    get(_target, property) {
      const view = new DataView(memory.buffer);
      const value = Reflect.get(view, property, view);
      return typeof value === "function" ? value.bind(view) : value;
    },
  });
}
