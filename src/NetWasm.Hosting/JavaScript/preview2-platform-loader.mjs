import { isAbsolute, relative, resolve, sep } from "node:path";
import { pathToFileURL } from "node:url";

import { parseStrictJson } from "./strict-json-reader.mjs";

const factoryKeys = ["hashBytes", "importModule", "readFile", "realPath"];
const packageKeys = ["id", "rootPath", "sha256", "version"];
const manifestKeys = ["assets", "packageId", "packageVersion", "schemaVersion"];
const assetKeys = ["id", "relativePath", "sha256", "version"];
const policyKeys = [
  "acceptedInvocation",
  "compilerClosure",
  "contract",
  "entryPoint",
  "evidence",
  "excludedPackagePaths",
  "lockSha256",
  "requiredCommands",
  "roots",
  "runtimeShimClosure",
  "schemaVersion",
  "selectedPackagePaths",
];
const closureKeys = ["files", "schemaVersion"];
const closureFileKeys = ["path", "sha256"];
const digestPattern = /^[0-9a-f]{64}$/u;
const toolchainId = "NetWasm.Toolchain";
const toolchainManifestPath = "tools/toolchain-manifest.json";
const jcoRootPath = "tools/jco";
const closureAssetId = "jco.closure-integrity";
const policyAssetId = "jco.closure-policy";
const shimPackageAssetId = "preview2-shim.package";
const shimPackageName = "@bytecodealliance/preview2-shim";
const shimPackagePrefix = "node_modules/@bytecodealliance/preview2-shim";

export function createPreview2PlatformLoader(options) {
  assertExactDataObject(options, factoryKeys, "Preview 2 platform loader options");
  for (const [name, value] of Object.entries(options)) {
    if (typeof value !== "function") {
      throw new TypeError(`Preview 2 platform loader '${name}' action is required`);
    }
  }

  return Object.freeze(async function loadPreview2Platform(package_) {
    readToolPackage(package_);
    const packageRoot = resolve(package_.rootPath);
    if (packageRoot !== package_.rootPath) {
      throw new TypeError("Toolchain package root must be canonical and absolute");
    }
    const physicalRoot = await options.realPath(packageRoot);
    if (typeof physicalRoot !== "string" || !isAbsolute(physicalRoot)) {
      throw new TypeError("Toolchain package physical root is invalid");
    }

    const manifestBytes = await readContainedFile(
      packageRoot,
      physicalRoot,
      toolchainManifestPath,
      options);
    const manifest = readManifest(decode(manifestBytes), package_);
    const assets = new Map(manifest.assets.map(asset => [asset.id, asset]));
    const [closureBytes, policyBytes, shimPackageBytes] = await Promise.all([
      readVerifiedAsset(assets, closureAssetId, packageRoot, physicalRoot, options),
      readVerifiedAsset(assets, policyAssetId, packageRoot, physicalRoot, options),
      readVerifiedAsset(assets, shimPackageAssetId, packageRoot, physicalRoot, options),
    ]);
    const policy = readPolicy(decode(policyBytes));
    const closure = readClosure(decode(closureBytes), policy.runtimeShimClosure);
    const shimPackage = readShimPackage(decode(shimPackageBytes), assets.get(shimPackageAssetId));
    const instantiationPath = resolveShimExport(
      shimPackage,
      "./instantiation",
      shimPackagePrefix);
    const filesystemPath = resolveShimExport(
      shimPackage,
      "./filesystem",
      shimPackagePrefix);
    requireClosureFile(closure, instantiationPath);
    requireClosureFile(closure, filesystemPath);

    await Promise.all(closure.files.map(file => readVerifiedFile(
      packageRoot,
      physicalRoot,
      `${jcoRootPath}/${file.path}`,
      file.sha256,
      options)));

    const [instantiation, filesystem] = await Promise.all([
      options.importModule(pathToFileURL(resolveContained(
        packageRoot,
        `${jcoRootPath}/${instantiationPath}`)).href),
      options.importModule(pathToFileURL(resolveContained(
        packageRoot,
        `${jcoRootPath}/${filesystemPath}`)).href),
    ]);
    if (instantiation === null || typeof instantiation !== "object"
        || typeof instantiation.WASIShim !== "function") {
      throw new TypeError("Toolchain Preview 2 instantiation module is incompatible");
    }
    if (filesystem === null || typeof filesystem !== "object"
        || typeof filesystem.createFilesystem !== "function") {
      throw new TypeError("Toolchain Preview 2 filesystem module is incompatible");
    }
    const WASIShim = instantiation.WASIShim;
    return Object.freeze({
      createFilesystem: filesystem.createFilesystem,
      createShim: config => new WASIShim(config),
    });
  });
}

async function readVerifiedAsset(assets, id, packageRoot, physicalRoot, options) {
  const asset = assets.get(id);
  if (asset === undefined) {
    throw new TypeError(`Toolchain manifest does not provide '${id}'`);
  }
  return readVerifiedFile(
    packageRoot,
    physicalRoot,
    asset.relativePath,
    asset.sha256,
    options);
}

async function readVerifiedFile(
  packageRoot,
  physicalRoot,
  relativePath,
  expectedDigest,
  options) {
  const bytes = await readContainedFile(
    packageRoot,
    physicalRoot,
    relativePath,
    options);
  const digest = await options.hashBytes(bytes);
  if (digest !== expectedDigest) {
    throw new TypeError(`Toolchain asset '${relativePath}' failed integrity validation`);
  }
  return bytes;
}

async function readContainedFile(packageRoot, physicalRoot, relativePath, options) {
  const path = resolveContained(packageRoot, relativePath);
  const physicalPath = await options.realPath(path);
  if (typeof physicalPath !== "string" || !isAbsolute(physicalPath)
      || isOutside(physicalRoot, physicalPath)) {
    throw new TypeError(`Toolchain asset '${relativePath}' escapes its package root`);
  }
  const bytes = await options.readFile(path);
  if (!(bytes instanceof Uint8Array)) {
    throw new TypeError(`Toolchain asset '${relativePath}' reader returned invalid bytes`);
  }
  return bytes;
}

function readToolPackage(value) {
  assertExactDataObject(value, packageKeys, "Toolchain package descriptor");
  if (!Object.isFrozen(value) || value.id !== toolchainId
      || typeof value.version !== "string" || value.version.length === 0
      || typeof value.rootPath !== "string" || !isAbsolute(value.rootPath)
      || typeof value.sha256 !== "string" || !digestPattern.test(value.sha256)) {
    throw new TypeError("Toolchain package descriptor is incompatible");
  }
}

function readManifest(text, package_) {
  const manifest = parseStrictJson(text);
  assertExactDataObject(manifest, manifestKeys, "Toolchain manifest");
  if (manifest.schemaVersion !== "1" || manifest.packageId !== package_.id
      || manifest.packageVersion !== package_.version
      || !Array.isArray(manifest.assets) || manifest.assets.length === 0) {
    throw new TypeError("Toolchain manifest identity is incompatible");
  }
  const identities = new Set();
  for (const asset of manifest.assets) {
    assertExactDataObject(asset, assetKeys, "Toolchain manifest asset");
    if (typeof asset.id !== "string" || asset.id.length === 0
        || typeof asset.version !== "string" || asset.version.length === 0
        || typeof asset.relativePath !== "string"
        || typeof asset.sha256 !== "string" || !digestPattern.test(asset.sha256)) {
      throw new TypeError("Toolchain manifest asset is incomplete");
    }
    resolveContained(package_.rootPath, asset.relativePath);
    if (identities.has(asset.id)) {
      throw new TypeError(`Toolchain manifest asset '${asset.id}' is duplicated`);
    }
    identities.add(asset.id);
  }
  return manifest;
}

function readPolicy(text) {
  const policy = parseStrictJson(text);
  assertExactDataObject(policy, policyKeys, "Toolchain closure policy");
  if (policy.schemaVersion !== "1" || policy.contract !== "jco-transpile-only"
      || !Array.isArray(policy.runtimeShimClosure)
      || policy.runtimeShimClosure.length === 0
      || !policy.runtimeShimClosure.includes(shimPackagePrefix)) {
    throw new TypeError("Toolchain runtime-shim policy is incompatible");
  }
  const prefixes = new Set();
  for (const prefix of policy.runtimeShimClosure) {
    validatePortableRelativePath(prefix, "Toolchain runtime-shim package path");
    if (prefix !== "node_modules" && !prefix.startsWith("node_modules/")) {
      throw new TypeError("Toolchain runtime-shim package path is outside node_modules");
    }
    if (prefixes.has(prefix)) {
      throw new TypeError(`Toolchain runtime-shim package '${prefix}' is duplicated`);
    }
    prefixes.add(prefix);
  }
  return Object.freeze({ runtimeShimClosure: Object.freeze([...prefixes]) });
}

function readClosure(text, runtimePrefixes) {
  const closure = parseStrictJson(text);
  assertExactDataObject(closure, closureKeys, "Toolchain closure integrity manifest");
  if (closure.schemaVersion !== "1"
      || !Array.isArray(closure.files) || closure.files.length === 0) {
    throw new TypeError("Toolchain closure integrity manifest is empty");
  }
  const paths = new Set();
  const coveredPrefixes = new Set();
  const files = [];
  for (const file of closure.files) {
    assertExactDataObject(file, closureFileKeys, "Toolchain closure file");
    validatePortableRelativePath(file.path, "Toolchain closure file path");
    if (typeof file.sha256 !== "string" || !digestPattern.test(file.sha256)) {
      throw new TypeError("Toolchain closure file digest is invalid");
    }
    if (paths.has(file.path)) {
      throw new TypeError(`Toolchain closure file '${file.path}' is duplicated`);
    }
    paths.add(file.path);
    const prefix = runtimePrefixes.find(
      value => file.path === value || file.path.startsWith(`${value}/`));
    if (prefix !== undefined) {
      coveredPrefixes.add(prefix);
      files.push(Object.freeze({ path: file.path, sha256: file.sha256 }));
    }
  }
  if (runtimePrefixes.some(prefix => !coveredPrefixes.has(prefix))) {
    throw new TypeError("Toolchain closure omits a runtime-shim package");
  }
  return Object.freeze({ files: Object.freeze(files), paths });
}

function readShimPackage(text, asset) {
  const package_ = parseStrictJson(text);
  if (package_ === null || typeof package_ !== "object" || Array.isArray(package_)
      || package_.name !== shimPackageName || package_.version !== asset.version
      || package_.exports === null || typeof package_.exports !== "object"
      || Array.isArray(package_.exports)) {
    throw new TypeError("Toolchain Preview 2 shim package is incompatible");
  }
  return package_;
}

function resolveShimExport(package_, subpath, packagePrefix) {
  const direct = package_.exports[subpath];
  const entry = direct ?? package_.exports["./*"];
  const selected = typeof entry?.node === "string"
    ? entry.node
    : entry?.node?.default;
  if (typeof selected !== "string" || !selected.startsWith("./")) {
    throw new TypeError(`Toolchain Preview 2 shim export '${subpath}' is incompatible`);
  }
  const target = direct === undefined
    ? selected.replaceAll("*", subpath.slice(2))
    : selected;
  const relativeTarget = target.slice(2);
  validatePortableRelativePath(relativeTarget, "Toolchain Preview 2 shim export path");
  return `${packagePrefix}/${relativeTarget}`;
}

function requireClosureFile(closure, path) {
  if (!closure.paths.has(path)) {
    throw new TypeError(`Toolchain closure omits Preview 2 module '${path}'`);
  }
}

function resolveContained(root, relativePath) {
  validatePortableRelativePath(relativePath, "Toolchain asset path");
  return resolve(root, ...relativePath.split("/"));
}

function validatePortableRelativePath(value, label) {
  if (typeof value !== "string" || value.length === 0 || value.includes("\\")
      || value.includes("\0") || isAbsolute(value)
      || value.split("/").some(segment => segment === "" || segment === "." || segment === "..")) {
    throw new TypeError(`${label} must be a canonical relative slash path`);
  }
}

function isOutside(root, path) {
  const value = relative(root, path);
  return value === ".." || value.startsWith(`..${sep}`)
    || isAbsolute(value);
}

function decode(bytes) {
  return new TextDecoder("utf-8", { fatal: true }).decode(bytes);
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
