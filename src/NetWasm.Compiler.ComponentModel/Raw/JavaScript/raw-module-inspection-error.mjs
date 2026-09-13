export class RawModuleInspectionError extends Error {
  constructor(code, options) {
    super(`Raw module inspection failed: ${code}.`, options);
    this.name = "RawModuleInspectionError";
    this.code = code;
  }
}
