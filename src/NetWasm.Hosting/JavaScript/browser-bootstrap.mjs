import { createBrowserBootstrapComposition } from "./browser-bootstrap-composition.mjs";
import { createBrowserNetWasmExecution } from "./browser-execution-root.mjs";

export function createBrowserNetWasmBootstrap(options) {
  return createBrowserBootstrapComposition(options, createBrowserNetWasmExecution);
}
