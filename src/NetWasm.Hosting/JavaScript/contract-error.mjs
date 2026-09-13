const contractErrors = new WeakSet();

export function createContractError(
  message = "NetWasm execution contract validation failed.") {
  const error = new Error(message);
  contractErrors.add(error);
  return error;
}

export function isContractError(value) {
  return value !== null && typeof value === "object" && contractErrors.has(value);
}
