import { commandExecutionContract as commandComponentContract } from "./execution-contracts.mjs";

export { commandComponentContract };

const binders = new Map([
  [commandComponentContract, bindCommand],
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
  const command = instance.command;
  if (command === null || typeof command !== "object" || typeof command.run !== "function") {
    throw new TypeError("canonical command component is incomplete");
  }
  const diagnosticCommand = instance.diagnosticCommand;
  if (diagnosticCommand !== undefined && (diagnosticCommand === null
      || typeof diagnosticCommand !== "object" || typeof diagnosticCommand.run !== "function")) {
    throw new TypeError("canonical diagnostic command component is incomplete");
  }
  return Object.freeze({
    contractKey: commandComponentContract,
    command: Object.freeze({ run: command.run.bind(command) }),
    ...(diagnosticCommand === undefined ? {} : {
      diagnosticCommand: Object.freeze({ run: diagnosticCommand.run.bind(diagnosticCommand) }),
    }),
  });
}
