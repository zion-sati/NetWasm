import { readManagedExceptionDetails } from "./managed-exception-details.mjs";

export const managedExceptionBrand = Symbol.for("NetWasm.ManagedException");

export class NetWasmHostError extends Error {
  constructor(message, { cause } = {}) {
    super(message, { cause });
    this.name = "NetWasmHostError";
  }
}

export class NetWasmManagedError extends Error {
  constructor(exportName, { cause, managedType, managed = null } = {}) {
    const details = managed === null ? null : readManagedExceptionDetails(managed);
    super(details?.message ?? details?.typeName ?? `managed export ${exportName} failed`, { cause });
    this.name = "NetWasmManagedError";
    this.exportName = exportName;
    this.managedType = details?.typeId ?? managedType;
    this.managed = details;
    this[managedExceptionBrand] = true;
  }
}
