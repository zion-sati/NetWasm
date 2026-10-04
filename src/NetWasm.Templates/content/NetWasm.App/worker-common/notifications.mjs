export function createWorkerImports(notify) {
  return Object.freeze({
    report(completed, total) {
      notify("progress", [completed, total]);
    },
  });
}
