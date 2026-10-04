import { createGuestWakeNotifier } from "./guest-wake-notifier.mjs";
import { createComponentExportSessionOpener } from "./component-export-session-opener.mjs";
import { createPollableReactor } from "./pollable-reactor.mjs";
import { createScopeReleaseCommand } from "./scope-release-command.mjs";

export const openComponentExportSession = createComponentExportSessionOpener(
  Object.freeze({
    createGuestNotifier: createGuestWakeNotifier,
    createReactor: createPollableReactor,
    createReleaseCommand: createScopeReleaseCommand,
  })).open;
