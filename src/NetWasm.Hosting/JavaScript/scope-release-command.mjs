export function createScopeReleaseCommand(releaseActions = []) {
  if (!Array.isArray(releaseActions)) {
    throw new TypeError("scope release actions must be an array");
  }
  const actions = Object.freeze(releaseActions.map(action => {
    if (typeof action !== "function") {
      throw new TypeError("scope release action is invalid");
    }
    return action;
  }));
  let completion;
  const close = () => {
    if (completion !== undefined) return completion;
    let resolveCompletion;
    let rejectCompletion;
    completion = new Promise((resolve, reject) => {
      resolveCompletion = resolve;
      rejectCompletion = reject;
    });
    releaseAll(actions).then(resolveCompletion, rejectCompletion);
    return completion;
  };
  return Object.freeze({ close });
}

async function releaseAll(actions) {
  const failures = [];
  for (let index = actions.length - 1; index >= 0; index--) {
    try {
      await actions[index]();
    } catch (cause) {
      failures.push(cause);
    }
  }
  return Object.freeze(failures);
}
