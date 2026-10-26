import {
  consoleLine,
  createLineOutput,
  mergeEnvironment,
  readEnvironmentOverride,
} from "./session-options.mjs";
import { bindWorkerImports } from "./worker-import-bindings.mjs";

const factoryKeys = [
  "configuration", "manifestUrl", "modules", "platform", "platformProviderCatalog", "snapshotDeploymentManifest",
];
const requestKeys = ["exports", "options", "startup"];
const optionNames = new Set(["environment", "modules", "onNotification", "platform", "stderr", "stdout"]);
const platformHookNames = new Set(["now", "random"]);
const wallClockModule = "wasi:clocks/wall-clock@0.2.11";
const randomModules = Object.freeze({
  "wasi:random/insecure@0.2.11": ["getInsecureRandomBytes", "getInsecureRandomU64"],
  "wasi:random/random@0.2.11": ["getRandomBytes", "getRandomU64"],
});
const insecureSeedModule = "wasi:random/insecure-seed@0.2.11";
const reservedExportNames = new Set([
  "__proto__",
  "close",
  "constructor",
  "failure",
  "prototype",
  "then",
]);

// Hosts one JsModule deployment in the page realm: exports are called directly,
// and import modules run beside the page, so they may use the DOM.
export function createPageSessionFactory(createExportSession) {
  if (typeof createExportSession !== "function") {
    throw new TypeError("page session export-session factory is required");
  }
  return function createPageSession(options) {
    assertExactDataObject(options, factoryKeys, "page session options");
    const bundled = snapshotNamespaces(options.modules, "bundled page module");

    return Object.freeze(async function openPage(request) {
      assertExactDataObject(request, requestKeys, "page session open request");
      const exportNames = snapshotExportNames(request.exports);
      const openOptions = readOpenOptions(request.options);
      let open = true;
      const notify = createNotifier(openOptions.onNotification, () => open);
      const consumerModules = bindModules(
        bundled,
        snapshotNamespaces(openOptions.modules ?? {}, "page module"),
        notify);
      const stdout = createLineOutput(openOptions.stdout ?? consoleLine("log"));
      const stderr = createLineOutput(openOptions.stderr ?? consoleLine("error"));
      const openSession = createExportSession(Object.freeze({
        configuration: mergeEnvironment(options.configuration,
          readEnvironmentOverride(openOptions.environment, "page session environment")),
        consumerModules,
        manifestUrl: options.manifestUrl,
        platform: withPlatformHooks(options.platform, openOptions.platform),
        platformProviderCatalog: options.platformProviderCatalog,
        snapshotDeploymentManifest: options.snapshotDeploymentManifest,
        stderr: stderr.sink,
        stdout: stdout.sink,
      }));
      const session = await openSession(Object.freeze({ startup: request.startup }));
      // The session settles failure (never rejects it) on a trap, fatal error or close.
      session.failure.then(() => {
        open = false;
        stdout.flush();
        stderr.flush();
      });
      const actual = Object.keys(session.exports).sort();
      if (actual.length !== exportNames.length
          || actual.some((name, index) => name !== exportNames[index])) {
        const mismatch = new TypeError("page session exports do not match the generated module");
        try {
          await session.close();
        } catch (cause) {
          throw new AggregateError([mismatch, cause], "page session startup failed");
        }
        throw mismatch;
      }
      const surface = {};
      for (const name of exportNames) surface[name] = session.exports[name];
      surface.close = () => {
        open = false;
        return session.close();
      };
      surface.failure = session.failure;
      return Object.freeze(surface);
    });
  };
}

function snapshotExportNames(value) {
  if (!Array.isArray(value) || value.some(name => typeof name !== "string"
      || name.length === 0 || reservedExportNames.has(name))) {
    throw new TypeError("page session export names are invalid");
  }
  const names = [...value].sort();
  if (names.some((name, index) => index !== 0 && name === names[index - 1])) {
    throw new TypeError("page session export names must be unique");
  }
  return Object.freeze(names);
}

function readOpenOptions(value) {
  if (value === undefined) return Object.freeze({});
  assertPlainDataObject(value, "page session open options");
  for (const name of Object.keys(value)) {
    if (!optionNames.has(name)) throw new TypeError(`page session open option '${name}' is unsupported`);
  }
  for (const name of ["onNotification", "stderr", "stdout"]) {
    if (value[name] !== undefined && typeof value[name] !== "function") {
      throw new TypeError(`page session ${name} must be a function`);
    }
  }
  if (value.platform !== undefined) {
    assertPlainDataObject(value.platform, "page session platform hooks");
    for (const [name, hook] of Object.entries(value.platform)) {
      if (!platformHookNames.has(name) || typeof hook !== "function") {
        throw new TypeError(`page session platform hook '${name}' is unsupported`);
      }
    }
  }
  return value;
}

// Test hooks replace the wall clock and the random sources the WASI shim supplies.
function withPlatformHooks(platform, hooks) {
  if (hooks === undefined) return platform;
  return Object.freeze({
    ...platform,
    createShim(configuration) {
      const shim = platform.createShim(configuration);
      return Object.freeze({
        getImportObject(request) {
          return applyPlatformHooks(shim.getImportObject(request), hooks);
        },
      });
    },
  });
}

function applyPlatformHooks(imports, hooks) {
  const result = { ...imports };
  if (hooks.now !== undefined && result[wallClockModule] !== undefined) {
    result[wallClockModule] = Object.freeze({ ...result[wallClockModule], now: () => readDatetime(hooks.now()) });
  }
  if (hooks.random !== undefined) {
    const bytes = length => {
      const value = new Uint8Array(Number(length));
      hooks.random(value);
      return value;
    };
    const u64 = () => new DataView(bytes(8).buffer).getBigUint64(0, true);
    for (const [module, [bytesName, u64Name]] of Object.entries(randomModules)) {
      if (result[module] !== undefined) {
        result[module] = Object.freeze({ ...result[module], [bytesName]: bytes, [u64Name]: u64 });
      }
    }
    if (result[insecureSeedModule] !== undefined) {
      result[insecureSeedModule] = Object.freeze({ ...result[insecureSeedModule], insecureSeed: () => [u64(), u64()] });
    }
  }
  return result;
}

function readDatetime(milliseconds) {
  if (typeof milliseconds !== "number" || !Number.isFinite(milliseconds) || milliseconds < 0) {
    throw new TypeError("page session now() must return non-negative epoch milliseconds");
  }
  const seconds = Math.floor(milliseconds / 1000);
  return { seconds: BigInt(seconds), nanoseconds: Math.round((milliseconds - seconds * 1000) * 1e6) };
}

// Accepts module namespaces (`import * as host from ...`) and plain objects;
// only function members are import candidates.
function snapshotNamespaces(value, label) {
  assertPlainDataObject(value, `${label} map`);
  const modules = new Map();
  for (const [moduleName, namespace] of Object.entries(value)) {
    if (namespace === null || typeof namespace !== "object") {
      throw new TypeError(`${label} '${moduleName}' must be a module namespace`);
    }
    const members = Object.create(null);
    for (const [name, member] of Object.entries(namespace)) {
      if (typeof member === "function") members[name] = member;
    }
    modules.set(moduleName, members);
  }
  return modules;
}

function bindModules(bundled, supplied, notify) {
  const modules = {};
  for (const [moduleName, members] of [...bundled, ...supplied]) {
    if (Object.hasOwn(modules, moduleName)) {
      throw new TypeError(`page module '${moduleName}' is bundled and also supplied to open()`);
    }
    const imports = bindWorkerImports(members, notify);
    modules[moduleName] = Object.freeze(Object.fromEntries(Object.entries(imports)
      .filter(([name]) => name !== "createWorkerImports")));
  }
  return Object.freeze(modules);
}

// Mirrors worker notifications: one-way, delivered after the import returns,
// so a handler never runs inside a guest call.
function createNotifier(handler, isOpen) {
  return function notify(operation, arguments_ = []) {
    if (typeof operation !== "string" || operation.length === 0 || !Array.isArray(arguments_)) {
      throw new TypeError("page notification requires a name and argument array");
    }
    if (handler === undefined || !isOpen()) return;
    const value = Object.freeze({ operation, arguments: Object.freeze([...arguments_]) });
    queueMicrotask(() => {
      try {
        Promise.resolve(handler(value)).catch(reportNotificationError);
      } catch (cause) {
        reportNotificationError(cause);
      }
    });
  };
}

function reportNotificationError(cause) {
  if (typeof globalThis.reportError === "function") globalThis.reportError(cause);
  else console.error(cause);
}

function assertPlainDataObject(value, label) {
  if (value === null || typeof value !== "object" || Array.isArray(value)
      || Object.getOwnPropertySymbols(value).length !== 0) {
    throw new TypeError(`${label} is invalid`);
  }
  const prototype = Object.getPrototypeOf(value);
  if (prototype !== null && prototype !== Object.prototype
      || Object.values(Object.getOwnPropertyDescriptors(value))
        .some(descriptor => !descriptor.enumerable || !("value" in descriptor))) {
    throw new TypeError(`${label} must be a plain data object`);
  }
}

function assertExactDataObject(value, keys, label) {
  assertPlainDataObject(value, label);
  const actualKeys = Object.keys(value).sort();
  if (actualKeys.length !== keys.length
      || actualKeys.some((key, index) => key !== keys[index])) {
    throw new TypeError(`${label} shape is invalid`);
  }
}
