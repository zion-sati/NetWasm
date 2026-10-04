import { createBrowserArtifactTransport } from "./browser-artifact-transport.mjs";
import { createBrowserDeploymentManifestLoader } from "./browser-deployment-manifest-loader.mjs";
import { createBrowserRawLoader } from "./browser-raw-loader.mjs";
import { createBrowserTimeZoneMaterializer } from "./browser-timezone-materializer.mjs";
import { createBrowserVerifiedModuleImporter } from "./browser-verified-module-importer.mjs";
import { createExecutionRequestValidator } from "./execution-request-validator.mjs";
import { jsExportWorkerExecutionContract } from "./execution-contracts.mjs";
import { createManagedExceptionOutput } from "./managed-exception-output.mjs";
import { createOutputFinalization } from "./output-finalization.mjs";
import { createPreview2FilesystemScope } from "./preview2-filesystem-scope.mjs";
import { createProviderExecutionComposition } from "./provider-execution-composition.mjs";
import { selectProviderKind } from "./provider-kind-selector.mjs";
import { prepareRawNetWasmInterop } from "./raw-interop-preparation.mjs";
import { openRawExportSession } from "./raw-export-session.mjs";
import { createScopeReleaseCommand } from "./scope-release-command.mjs";
import { selectTimeZoneSidecar } from "./timezone-sidecar-selector.mjs";

const optionKeys = ["configuration", "consumerModules", "manifestUrl", "platform", "stderr", "stdout"];
const configurationKeys = ["applicationImports", "arguments", "environment", "grants"];
const platformKeys = [
  "compileCoreModule",
  "createFilesystem",
  "createModuleUrl",
  "createShim",
  "digest",
  "fetch",
  "importModule",
  "revokeModuleUrl",
];
const openKeys = ["startup"];
const startupKeys = ["buildFingerprint", "manifestSha256"];
const builtinInteropModule = "netwasm.host.v1";

export function createBrowserRawExportSession(options) {
  assertExactDataObject(options, optionKeys, "browser raw export-session options");
  assertExactDataObject(options.configuration, configurationKeys,
    "browser raw export-session configuration");
  assertExactDataObject(options.platform, platformKeys, "browser raw export-session platform");
  for (const [name, value] of Object.entries(options.platform)) {
    if (typeof value !== "function") {
      throw new TypeError(`browser raw export-session platform '${name}' action is required`);
    }
  }
  const consumerModules = snapshotConsumerModules(options.consumerModules);
  const transport = createBrowserArtifactTransport({
    digest: options.platform.digest,
    fetch: options.platform.fetch,
    manifestUrl: options.manifestUrl,
  });
  const importModule = createBrowserVerifiedModuleImporter({
    createModuleUrl: options.platform.createModuleUrl,
    importModule: options.platform.importModule,
    revokeModuleUrl: options.platform.revokeModuleUrl,
  });
  const loadManifest = createBrowserDeploymentManifestLoader({
    manifestUrl: options.manifestUrl,
    platform: Object.freeze({
      digest: options.platform.digest,
      fetch: options.platform.fetch,
    }),
  });
  const loadArtifacts = createBrowserRawLoader({
    manifestUrl: options.manifestUrl,
    platform: Object.freeze({
      compileCoreModule: options.platform.compileCoreModule,
      createModuleUrl: options.platform.createModuleUrl,
      digest: options.platform.digest,
      fetch: options.platform.fetch,
      importModule: options.platform.importModule,
      revokeModuleUrl: options.platform.revokeModuleUrl,
    }),
  });
  const prepareProviders = createProviderExecutionComposition({
    createShim: options.platform.createShim,
    hashBytes: transport.hashBytes,
    importModule,
    isAbsoluteHostPath: rejectHostPath,
    readArtifact: transport.readArtifact,
  });
  const validateRequest = createExecutionRequestValidator({
    isAbsoluteHostPath: rejectHostPath,
    selectProviderKind,
  });

  return Object.freeze(async function openBrowserRawExportSession(request) {
    assertExactDataObject(request, openKeys, "browser raw export-session open request");
    const startup = snapshotStartup(request.startup);
    const output = createOutputFinalization({ stderr: options.stderr, stdout: options.stdout });
    let releaseActions = output.releaseActions.map(action => action.release);
    try {
      const hostRequest = validateRequest({
        ...options.configuration,
        buildFingerprint: startup.buildFingerprint,
        deploymentManifestSha256: startup.manifestSha256,
        schemaVersion: 1,
      });
      const manifest = await loadManifest(Object.freeze({
        expectedSha256: startup.manifestSha256,
        signal: null,
      }));
      if (manifest.buildFingerprint !== startup.buildFingerprint
          || manifest.deploymentKind !== "raw"
          || manifest.executionContract !== jsExportWorkerExecutionContract) {
        throw new TypeError("browser raw export-session deployment identity is invalid");
      }
      const loaded = await loadArtifacts(Object.freeze({
        artifacts: manifest.artifacts,
        signal: null,
      }));
      if (loaded.abi.target !== manifest.target) {
        throw new TypeError("browser raw export-session target is invalid");
      }
      validateConsumerModules(loaded.interopManifest, consumerModules);

      const filesystem = createPreview2FilesystemScope({
        createFilesystem: options.platform.createFilesystem,
        preopens: hostRequest.grants.preopens,
      });
      releaseActions.push(filesystem.dispose);
      const selection = selectTimeZoneSidecar({
        artifacts: manifest.artifacts,
        environment: hostRequest.environment,
        runtimeFeatures: manifest.runtimeFeatures,
      });
      const materializeTimeZone = createBrowserTimeZoneMaterializer({
        manifestUrl: options.manifestUrl,
        platform: Object.freeze({
          digest: options.platform.digest,
          fetch: options.platform.fetch,
          mountReadOnlyFile: filesystem.mountReadOnlyFile,
        }),
      });
      const mount = await materializeTimeZone(Object.freeze({ selection, signal: null }));
      if (mount !== null) releaseActions.push(mount.release);

      const providers = await prepareProviders(Object.freeze({
        filesystem,
        manifest,
        request: hostRequest,
        signal: null,
        stderr: output.stderr,
        stdout: output.stdout,
      }));
      const ownedReleaseActions = releaseActions;
      releaseActions = null;
      return await openRawExportSession({
        abi: loaded.abi,
        adapter: loaded.adapter,
        instantiate: value => WebAssembly.instantiate(value.module, value.imports),
        manifest: loaded.interopManifest,
        module: loaded.module,
        prepareInterop: lifetime => prepareRawNetWasmInterop({
          ...lifetime,
          consumerModules,
          diagnosticArtifacts: loaded.diagnosticArtifacts,
          manifest: loaded.interopManifest,
          managedExceptionReporting: createManagedExceptionOutput(output.stderr),
          runtimeModules: Object.freeze(Object.create(null)),
          stackTraceSymbols: loaded.stackTraceSymbols,
        }),
        providers: providers.rawProviders,
        releaseActions: ownedReleaseActions,
      });
    } catch (cause) {
      if (releaseActions === null) throw cause;
      const failures = await createScopeReleaseCommand(releaseActions).close();
      if (failures.length !== 0) {
        throw new AggregateError([cause, ...failures],
          "browser raw export-session startup failed");
      }
      throw cause;
    }
  });
}

function rejectHostPath() {
  return false;
}

function snapshotConsumerModules(value) {
  assertPlainDataObject(value, "browser raw export-session consumer modules");
  const modules = Object.create(null);
  for (const [moduleName, moduleValue] of Object.entries(value)) {
    if (moduleName === builtinInteropModule || selectProviderKind({ module: moduleName }) !== "application") {
      throw new TypeError(`worker JS import module '${moduleName}' is reserved`);
    }
    assertPlainDataObject(moduleValue, `worker JS import module '${moduleName}'`);
    const members = Object.create(null);
    for (const [name, member] of Object.entries(moduleValue)) {
      if (name.length === 0 || typeof member !== "function") {
        throw new TypeError(`worker JS import module '${moduleName}' is invalid`);
      }
      members[name] = member;
    }
    modules[moduleName] = Object.freeze(members);
  }
  return Object.freeze(modules);
}

function validateConsumerModules(manifest, consumerModules) {
  const required = new Set(manifest.imports
    .map(descriptor => descriptor.module)
    .filter(moduleName => moduleName !== builtinInteropModule));
  const supplied = Object.keys(consumerModules);
  if (required.size !== supplied.length || supplied.some(moduleName => !required.has(moduleName))) {
    throw new TypeError("worker JS import modules do not match the interop manifest");
  }
}

function snapshotStartup(value) {
  assertExactDataObject(value, startupKeys, "browser raw export-session startup");
  for (const [name, digest] of Object.entries(value)) {
    if (typeof digest !== "string" || !/^[0-9a-f]{64}$/u.test(digest)) {
      throw new TypeError(`browser raw export-session ${name} is invalid`);
    }
  }
  return Object.freeze({
    buildFingerprint: value.buildFingerprint,
    manifestSha256: value.manifestSha256,
  });
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
