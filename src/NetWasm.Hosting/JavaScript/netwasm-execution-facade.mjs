// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: MIT

export function createNetWasmExecutionFacade(executeDeployment) {
  if (typeof executeDeployment !== "function") {
    throw new TypeError("deployment execution action is required");
  }
  return Object.freeze(async function executeNetWasm(request) {
    return executeDeployment(request);
  });
}
