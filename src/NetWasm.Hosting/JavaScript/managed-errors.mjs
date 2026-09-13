export const managedExceptionBrand = Symbol.for("NetWasm.ManagedException");

export class NetWasmHostError extends Error {
  constructor(message, { cause } = {}) {
    super(message, { cause });
    this.name = "NetWasmHostError";
  }
}

export class NetWasmManagedError extends Error {
  constructor(exportName, { cause, managedType } = {}) {
    super(`managed export ${exportName} failed`, { cause });
    this.name = "NetWasmManagedError";
    this.exportName = exportName;
    this.managedType = managedType;
    this[managedExceptionBrand] = true;
  }
}
