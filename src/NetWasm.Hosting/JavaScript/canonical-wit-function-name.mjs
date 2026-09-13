const identifier = "[a-z0-9][a-z0-9-]*";
const token = "[a-z0-9][a-z0-9.-]*";
const member = `\\[(?:method|static)\\]${identifier}\\.${identifier}`;
const intrinsic = `\\[(?:constructor|resource-drop|resource-dtor|export-resource-(?:new|rep|drop))\\]${identifier}`;
const functionNamePattern = new RegExp(`^(?:${token}|${member}|${intrinsic})$`, "u");

export function isCanonicalWitFunctionName(value) {
  return typeof value === "string" && functionNamePattern.test(value);
}
