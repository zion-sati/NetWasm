// Runs a JavaScript module's event loop after each call into the module. The module's event
// loop export runs the ready work and reports EXIT, or WAIT on a waitable set whose events the
// instance's Component Model hands to the loop's callback until it exits. A call into the
// module while the loop waits gives the loop a turn without an event, and another run once the
// wait ends, so work the call queued always runs.

const callbackExit = 0;

export function createRawEventPump(request) {
  if (request === null || typeof request !== "object"
      || request.componentModel === null || typeof request.componentModel !== "object"
      || typeof request.componentModel.drive !== "function"
      || typeof request.componentModel.wake !== "function"
      || typeof request.run !== "function" || typeof request.callback !== "function"
      || typeof request.onFailure !== "function") {
    throw new TypeError("raw event pump request is invalid");
  }
  const { componentModel, run, callback, onFailure } = request;
  let driving = false;
  let again = false;
  let stopped = false;

  const fail = error => {
    if (stopped) return;
    stopped = true;
    onFailure(error);
  };

  function turn() {
    if (stopped) return;
    if (driving) {
      again = true;
      componentModel.wake();
      return;
    }
    let code;
    try {
      code = run();
    } catch (error) {
      fail(error);
      return;
    }
    if ((code & 0xf) === callbackExit) return;
    driving = true;
    componentModel.drive(code, callback).then(() => {
      driving = false;
      if (!again) return;
      again = false;
      turn();
    }, error => {
      driving = false;
      fail(error);
    });
  }

  return Object.freeze({
    turn,
    stop() { stopped = true; },
  });
}
