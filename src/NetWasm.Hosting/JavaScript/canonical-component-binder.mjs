import {
  commandExecutionContract as commandComponentContract,
  processExecutionContract as processComponentContract,
} from "./execution-contracts.mjs";

export { commandComponentContract, processComponentContract };

const binders = new Map([
  [commandComponentContract, bindCommand],
  [processComponentContract, bindProcess],
]);

export function bindCanonicalComponent(request = {}) {
  if (request === null || typeof request !== "object") {
    throw new TypeError("canonical component binding request is required");
  }
  const { contractKey, instance } = request;
  if (typeof contractKey !== "string" || contractKey.length === 0) {
    throw new TypeError("component execution contract key is required");
  }
  if (instance === null || typeof instance !== "object") {
    throw new TypeError("canonical component instance is required");
  }
  const bind = binders.get(contractKey);
  if (bind === undefined) {
    throw new TypeError("component execution contract is unsupported");
  }
  return bind(instance);
}

function bindCommand(instance) {
  if (Object.hasOwn(instance, "process") || Object.hasOwn(instance, "reactorGuest")) {
    throw new TypeError("canonical component mixes command and process contracts");
  }
  const command = instance.command;
  if (command === null || typeof command !== "object" || typeof command.run !== "function") {
    throw new TypeError("canonical command component is incomplete");
  }
  return Object.freeze({
    contractKey: commandComponentContract,
    command: Object.freeze({ run: command.run.bind(command) }),
  });
}

function bindProcess(instance) {
  if (Object.hasOwn(instance, "command")) {
    throw new TypeError("canonical component mixes process and command contracts");
  }
  const process = instance.process;
  const reactorGuest = instance.reactorGuest;
  if (process === null || typeof process !== "object"
      || reactorGuest === null || typeof reactorGuest !== "object") {
    throw new TypeError("canonical process component is incomplete");
  }
  for (const operation of ["start", "status", "exitCode", "complete"]) {
    if (typeof process[operation] !== "function") {
      throw new TypeError("canonical process component is incomplete");
    }
  }
  if (typeof reactorGuest.wake !== "function") {
    throw new TypeError("canonical process reactor guest is incomplete");
  }
  return Object.freeze({
    contractKey: processComponentContract,
    process: Object.freeze({
      start: process.start.bind(process),
      status: process.status.bind(process),
      exitCode: process.exitCode.bind(process),
      complete: process.complete.bind(process),
    }),
    reactorGuest: Object.freeze({ wake: reactorGuest.wake.bind(reactorGuest) }),
  });
}
