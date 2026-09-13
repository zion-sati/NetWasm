import { createBrowserBootstrapComposition } from "./browser-bootstrap-composition.mjs";
import { createBrowserRawNetWasmExecution } from "./browser-raw-execution-root.mjs";

export function createBrowserNetWasmBootstrap(options) {
  return createBrowserBootstrapComposition(
    options,
    createBrowserRawNetWasmExecution);
}
