// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: MIT

import { createComponentExportSessionOpener } from "./component-export-session-opener.mjs";
import { createScopeReleaseCommand } from "./scope-release-command.mjs";

export const openComponentExportSession = createComponentExportSessionOpener(
  Object.freeze({
    createReleaseCommand: createScopeReleaseCommand,
  })).open;
