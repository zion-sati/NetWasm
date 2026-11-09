import {
  bindRawCanonicalImportInstance,
  closeRawCanonicalImportBinding,
  createRawCanonicalImportBinding,
  readRawComponentModel,
} from "./raw-canonical-import-binding.mjs";
import { createRawEventPump } from "./raw-event-pump.mjs";
import { createRawExportSessionOpener } from "./raw-export-session-opener.mjs";
import { composeRawImports } from "./raw-import-composer.mjs";
import { buildRawLibraryAbiPlan } from "./raw-library-abi-plan-builder.mjs";
import { createScopeReleaseCommand } from "./scope-release-command.mjs";

export const openRawExportSession = createRawExportSessionOpener(Object.freeze({
  bindCanonicalInstance: bindRawCanonicalImportInstance,
  buildPlan: buildRawLibraryAbiPlan,
  closeCanonicalBinding: closeRawCanonicalImportBinding,
  composeImports: composeRawImports,
  createCanonicalBinding: createRawCanonicalImportBinding,
  createEventPump: createRawEventPump,
  createReleaseCommand: createScopeReleaseCommand,
  readComponentModel: readRawComponentModel,
})).open;
