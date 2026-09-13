import { dirname, isAbsolute, join, resolve } from "node:path";

const factoryKeys = ["hashBytes", "readFile", "realPath"];
const packageKeys = ["id", "rootPath", "sha256", "version"];
const packageIdPattern = /^[A-Za-z0-9][A-Za-z0-9_.-]*$/u;
const packageVersionPattern = /^[0-9A-Za-z][0-9A-Za-z.+-]*$/u;
const digestPattern = /^[0-9a-f]{64}$/u;

export function createExecutionPackageVerifier(options) {
  assertExactDataObject(options, factoryKeys, "execution package verifier options");
  for (const [name, value] of Object.entries(options)) {
    if (typeof value !== "function") {
      throw new TypeError(`execution package verifier '${name}' action is required`);
    }
  }

  return Object.freeze(async function verifyExecutionPackage(package_) {
    readPackage(package_);
    const root = resolve(package_.rootPath);
    if (root !== package_.rootPath) {
      throw new TypeError("execution package root must be canonical and absolute");
    }
    const archiveName = `${package_.id}.${package_.version}.nupkg`.toLowerCase();
    const archivePath = join(root, archiveName);
    const [physicalRoot, physicalArchive] = await Promise.all([
      options.realPath(root),
      options.realPath(archivePath),
    ]);
    if (typeof physicalRoot !== "string" || !isAbsolute(physicalRoot)
        || typeof physicalArchive !== "string" || !isAbsolute(physicalArchive)
        || dirname(physicalArchive) !== physicalRoot) {
      throw new TypeError("execution package archive escapes its restored root");
    }
    const bytes = await options.readFile(archivePath);
    if (!(bytes instanceof Uint8Array)) {
      throw new TypeError("execution package archive reader returned invalid bytes");
    }
    const digest = await options.hashBytes(bytes);
    if (digest !== package_.sha256) {
      throw new TypeError("execution package archive failed integrity validation");
    }
    return package_;
  });
}

function readPackage(value) {
  assertExactDataObject(value, packageKeys, "execution package descriptor");
  if (!Object.isFrozen(value)
      || typeof value.id !== "string" || !packageIdPattern.test(value.id)
      || typeof value.version !== "string" || !packageVersionPattern.test(value.version)
      || typeof value.rootPath !== "string" || !isAbsolute(value.rootPath)
      || typeof value.sha256 !== "string" || !digestPattern.test(value.sha256)) {
    throw new TypeError("execution package descriptor is incompatible");
  }
}

function assertExactDataObject(value, keys, label) {
  if (value === null || typeof value !== "object" || Array.isArray(value)
      || Object.getOwnPropertySymbols(value).length !== 0) {
    throw new TypeError(`${label} is invalid`);
  }
  const prototype = Object.getPrototypeOf(value);
  const descriptors = Object.getOwnPropertyDescriptors(value);
  const actualKeys = Object.keys(descriptors).sort();
  if (prototype !== null && prototype !== Object.prototype
      || actualKeys.length !== keys.length
      || actualKeys.some((key, index) => key !== keys[index])
      || Object.values(descriptors).some(
        descriptor => !descriptor.enumerable || !("value" in descriptor))) {
    throw new TypeError(`${label} shape is invalid`);
  }
}
