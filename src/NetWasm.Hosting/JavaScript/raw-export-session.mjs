import { createGuestWakeNotifier } from "./guest-wake-notifier.mjs";
import { createPollableReactor } from "./pollable-reactor.mjs";
import {
  bindRawCanonicalImportInstance,
  closeRawCanonicalImportBinding,
  createRawCanonicalImportBinding,
} from "./raw-canonical-import-binding.mjs";
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
  createGuestNotifier: createGuestWakeNotifier,
  createReactor: createPollableReactor,
  createReleaseCommand: createScopeReleaseCommand,
})).open;
