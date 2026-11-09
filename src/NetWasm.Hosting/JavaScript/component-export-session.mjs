import { createComponentExportSessionOpener } from "./component-export-session-opener.mjs";
import { createScopeReleaseCommand } from "./scope-release-command.mjs";

export const openComponentExportSession = createComponentExportSessionOpener(
  Object.freeze({
    createReleaseCommand: createScopeReleaseCommand,
  })).open;
