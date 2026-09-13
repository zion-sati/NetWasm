const opaqueWakeFailure = Object.freeze({});
const maxUnsignedInt32 = 0xffffffff;

export function createGuestWakeNotifier({ guestWake, observeWake } = {}) {
  if (typeof guestWake !== "function") {
    throw new TypeError("guest wake action is required");
  }
  if (typeof observeWake !== "function") {
    throw new TypeError("process observation action is required");
  }

  return Object.freeze({
    notify(token) {
      if (!Number.isInteger(token) || token <= 0 || token > maxUnsignedInt32) {
        throw new TypeError("reactor token must be a nonzero unsigned 32-bit integer");
      }
      try {
        guestWake(token);
      } catch {
        observeWake(opaqueWakeFailure);
        return;
      }
      observeWake();
    },
  });
}
