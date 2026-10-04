import { createBrowserArtifactTransport } from "./browser-artifact-transport.mjs";
import { createBrowserComponentLoader } from "./browser-component-loader.mjs";
import { createBrowserDeploymentManifestLoader } from "./browser-deployment-manifest-loader.mjs";
import { createBrowserTimeZoneMaterializer } from "./browser-timezone-materializer.mjs";
import { createBrowserVerifiedModuleImporter } from "./browser-verified-module-importer.mjs";
import { openComponentExportSession } from "./component-export-session.mjs";
import { createExecutionRequestValidator } from "./execution-request-validator.mjs";
import { bindWorkerImports } from "./worker-import-bindings.mjs";
import { witWorkerExecutionContract } from "./execution-contracts.mjs";
import { createOutputFinalization } from "./output-finalization.mjs";
import { createPreview2FilesystemScope } from "./preview2-filesystem-scope.mjs";
import { createProviderExecutionComposition } from "./provider-execution-composition.mjs";
import { selectProviderKind } from "./provider-kind-selector.mjs";
import { createScopeReleaseCommand } from "./scope-release-command.mjs";
import { selectTimeZoneSidecar } from "./timezone-sidecar-selector.mjs";

const optionKeys = ["configuration", "manifestUrl", "platform", "stderr", "stdout"];
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

export function createBrowserComponentExportSession(options) {
  assertExactDataObject(options, optionKeys, "browser component export-session options");
  assertExactDataObject(options.configuration, configurationKeys,
    "browser component export-session configuration");
  assertExactDataObject(options.platform, platformKeys,
    "browser component export-session platform");
  for (const [name, value] of Object.entries(options.platform)) {
    if (typeof value !== "function") {
      throw new TypeError(
        "browser component export-session platform '" + name + "' action is required");
    }
  }

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
  const loadComponent = createBrowserComponentLoader({
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
  const validateRequest = createExecutionRequestValidator({
    isAbsoluteHostPath: rejectHostPath,
    selectProviderKind,
  });

  return Object.freeze(async function openBrowserComponentExportSession(request) {
    assertExactDataObject(request, Object.hasOwn(request ?? {}, "notify")
      ? ["notify", ...openKeys] : openKeys, "browser component export-session open request");
    const startup = snapshotStartup(request.startup);
    const output = createOutputFinalization({
      stderr: options.stderr,
      stdout: options.stdout,
    });
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
          || manifest.deploymentKind !== "browser"
          || manifest.executionContract !== witWorkerExecutionContract) {
        throw new TypeError(
          "browser component export-session deployment identity is invalid");
      }
      const loaded = await loadComponent(Object.freeze({
        artifacts: manifest.artifacts,
        signal: null,
      }));

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
      const mount = await materializeTimeZone(Object.freeze({
        selection,
        signal: null,
      }));
      if (mount !== null) releaseActions.push(mount.release);

      const prepareProviders = createProviderExecutionComposition({
        createShim: options.platform.createShim,
        hashBytes: transport.hashBytes,
        importModule: async (...arguments_) => bindWorkerImports(
          await importModule(...arguments_), request.notify),
        isAbsoluteHostPath: rejectHostPath,
        readArtifact: transport.readArtifact,
      });
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
      return await openComponentExportSession({
        adapter: loaded.adapter,
        imports: providers.componentImports,
        loadCoreModule: loaded.loadCoreModule,
        releaseActions: ownedReleaseActions,
      });
    } catch (cause) {
      if (releaseActions === null) throw cause;
      const failures = await createScopeReleaseCommand(releaseActions).close();
      if (failures.length !== 0) {
        throw new AggregateError(
          [cause, ...failures],
          "browser component export-session startup failed");
      }
      throw cause;
    }
  });
}

function rejectHostPath() {
  return false;
}

function snapshotStartup(value) {
  assertExactDataObject(value, startupKeys, "browser component export-session startup");
  for (const [name, digest] of Object.entries(value)) {
    if (typeof digest !== "string" || !/^[0-9a-f]{64}$/u.test(digest)) {
      throw new TypeError(
        "browser component export-session " + name + " is invalid");
    }
  }
  return Object.freeze({
    buildFingerprint: value.buildFingerprint,
    manifestSha256: value.manifestSha256,
  });
}

function assertExactDataObject(value, keys, label) {
  if (value === null || typeof value !== "object" || Array.isArray(value)
      || Object.getOwnPropertySymbols(value).length !== 0) {
    throw new TypeError(label + " is invalid");
  }
  const prototype = Object.getPrototypeOf(value);
  const descriptors = Object.getOwnPropertyDescriptors(value);
  const actualKeys = Object.keys(descriptors).sort();
  if (prototype !== null && prototype !== Object.prototype
      || actualKeys.length !== keys.length
      || actualKeys.some((key, index) => key !== keys[index])
      || Object.values(descriptors).some(
        descriptor => !descriptor.enumerable || !("value" in descriptor))) {
    throw new TypeError(label + " shape is invalid");
  }
}
