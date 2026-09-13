import { createBrowserBootstrapComposition } from "./browser-bootstrap-composition.mjs";
import { createBrowserComponentNetWasmExecution } from "./browser-component-execution-root.mjs";

export function createBrowserNetWasmBootstrap(options) {
  return createBrowserBootstrapComposition(
    options,
    createBrowserComponentNetWasmExecution);
}
