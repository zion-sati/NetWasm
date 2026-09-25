import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import test from "node:test";
import { chromium, firefox, webkit } from "playwright";

const repository = new URL("../../../", import.meta.url);
const shimRoot = new URL("node_modules/@bytecodealliance/preview2-shim/", repository);
const shimPackage = JSON.parse(await readFile(new URL("package.json", shimRoot)));
const browserEntry = shimPackage.exports["./*"].default.replace("*", "filesystem").replace(/^\.\//u, "");
const origin = "https://netwasm-provider.test";
const hostingFiles = new Set([
  "preview2-filesystem.mjs", "read-only-file-directory.mjs", "resource-disposal.mjs",
  "timezone-execution.mjs", "execution-scope-closer.mjs", "execution-result.mjs",
  "browser-timezone-materializer.mjs", "browser-artifact-transport.mjs",
  "timezone-sidecar-materializer.mjs", "timezone-sidecar-selector.mjs", "deployment-artifact.mjs",
]);

for (const [name, engine] of Object.entries({ chromium, firefox, webkit })) {
  test(`the production filesystem preserves read-only ownership in ${name}`, async () => {
    const browser = await engine.launch();
    const unexpectedRequests = [];
    let sidecarRequests = 0;
    try {
      const page = await browser.newPage();
      await page.route(`${origin}/**`, async route => {
        const path = new URL(route.request().url()).pathname;
        if (path === "/") {
          const importMap = { imports: {
            "@bytecodealliance/preview2-shim/filesystem": `${origin}/shim/${browserEntry}`,
          } };
          await route.fulfill({ contentType: "text/html", body:
            `<!doctype html><meta charset="utf-8"><script type="importmap">${JSON.stringify(importMap)}</script>` });
          return;
        }
        if (path === "/deploy/program.wasm.tz-info") {
          sidecarRequests++;
          await route.fulfill({ contentType: "application/octet-stream", body: Buffer.from([4, 5, 6]) });
          return;
        }
        let file;
        if (path.startsWith("/hosting/") && hostingFiles.has(path.slice("/hosting/".length))) {
          file = new URL(`src/NetWasm.Hosting/JavaScript/${path.slice("/hosting/".length)}`, repository);
        } else if (/^\/shim\/dist\/browser\/[a-zA-Z0-9_-]+\.js$/u.test(path)) {
          file = new URL(path.slice("/shim/".length), shimRoot);
        } else {
          unexpectedRequests.push("unmapped-asset");
          await route.abort();
          return;
        }
        await route.fulfill({ contentType: "text/javascript", body: await readFile(file, "utf8") });
      });
      await page.goto(origin);
      const result = await page.evaluate(async () => {
        const originalDispose = Symbol.dispose;
        const { createPreview2Filesystem } = await import("/hosting/preview2-filesystem.mjs");
        const { resourceDisposeSymbol } = await import("/hosting/resource-disposal.mjs");
        const shim = await import("@bytecodealliance/preview2-shim/filesystem");
        const source = shim.createFilesystem({
          adapter: new shim.InMemoryFilesystemAdapter(),
          preopens: { "/consumer": { dir: { "user.txt": { source: new Uint8Array([1, 2, 3]) } } } },
        });
        const sourceType = source.types.Descriptor;
        const fs = createPreview2Filesystem({ filesystem: source });
        const capture = action => {
          try { action(); return "no-error"; } catch (error) {
            return typeof error === "string" ? error : "unexpected-error";
          }
        };
        try {
          fs.mountReadOnlyFile({ guestPath: "/internal/asset", bytes: new Uint8Array([4, 5, 6]) });
          const [[consumer], [internal]] = fs.preopens.getDirectories();
          const userFile = consumer.openAt({}, "user.txt", {}, { read: true, write: true });
          const asset = internal.openAt({}, "asset", {}, { read: true });
          const otherAsset = internal.openAt({}, "asset", {}, { read: true });
          const result = {
            sameResourceClass: userFile instanceof fs.types.Descriptor && asset instanceof fs.types.Descriptor,
            sameFileIdentity: asset.isSameObject(otherAsset),
            separateIdentity: !asset.isSameObject(userFile) && !userFile.isSameObject(asset),
            sourceTypePreserved: source.types.Descriptor === sourceType,
            disposalContract: resourceDisposeSymbol === (Symbol.dispose ?? Symbol.for("dispose")),
            globalSymbolPreserved: Symbol.dispose === originalDispose,
            rejectedWrite: capture(() => asset.write(new Uint8Array([0]), 0n)),
            rejectedLink: capture(() => consumer.linkAt({}, "user.txt", internal, "new")),
          };
          userFile.write(new Uint8Array([9]), 0n);
          result.consumerBytes = [...userFile.read(3n, 0n)[0]];
          result.internalBytes = [...asset.read(3n, 0n)[0]];
          asset[resourceDisposeSymbol]();
          result.droppedDescriptor = capture(() => asset.read(1n, 0n));
          result.otherDescriptor = [...otherAsset.read(3n, 0n)[0]];
          fs.dispose();
          result.releasedInternal = capture(() => otherAsset.read(1n, 0n));
          result.releasedConsumer = capture(() => userFile.read(1n, 0n));
          result.sourcePreopens = source.preopens.getDirectories().length;
          return result;
        } finally {
          fs.dispose();
          source.dispose();
        }
      });
      assert.deepEqual(result, {
        sameResourceClass: true,
        sameFileIdentity: true,
        separateIdentity: true,
        sourceTypePreserved: true,
        disposalContract: true,
        globalSymbolPreserved: true,
        rejectedWrite: "read-only",
        rejectedLink: "read-only",
        consumerBytes: [9, 2, 3],
        internalBytes: [4, 5, 6],
        droppedDescriptor: "bad-descriptor",
        otherDescriptor: [4, 5, 6],
        releasedInternal: "bad-descriptor",
        releasedConsumer: "bad-descriptor",
        sourcePreopens: 1,
      });
      const lifetime = await page.evaluate(async () => {
        const { executeWithTimeZoneResources } = await import("/hosting/timezone-execution.mjs");
        const { createBrowserTimeZoneMaterializer } = await import("/hosting/browser-timezone-materializer.mjs");
        const { selectTimeZoneSidecar } = await import("/hosting/timezone-sidecar-selector.mjs");
        const { createPreview2Filesystem } = await import("/hosting/preview2-filesystem.mjs");
        const { normalExecutionResult } = await import("/hosting/execution-result.mjs");
        const shim = await import("@bytecodealliance/preview2-shim/filesystem");
        const digest = crypto.subtle.digest.bind(crypto.subtle);
        const sha256 = [...new Uint8Array(await digest("SHA-256", new Uint8Array([4, 5, 6])))]
          .map(byte => byte.toString(16).padStart(2, "0")).join("");
        const results = [];
        for (const mode of ["selected", "utc", "integrity", "execution-failure"]) {
          const calls = [];
          let scoped;
          let asset;
          const source = shim.createFilesystem({ adapter: new shim.InMemoryFilesystemAdapter(), preopens: {} });
          const selection = selectTimeZoneSidecar({
            runtimeFeatures: ["local-time"],
            artifacts: [
              { relativePath: "program.wasm", role: "application", mediaType: "application/wasm", sha256, schemaVersion: null },
              { relativePath: "program.wasm.tz-info", role: "timezone-data", mediaType: "application/octet-stream",
                sha256: mode === "integrity" ? "0".repeat(64) : sha256, schemaVersion: 1 },
            ],
            environment: [{ name: "TZ", value: mode === "utc" ? "UTC" : "Australia/Melbourne" }],
          });
          let readBytes = null;
          const outcome = await executeWithTimeZoneResources({
            selection,
            createFilesystem() {
              scoped = createPreview2Filesystem({ filesystem: source });
              return {
                ...scoped,
                mountReadOnlyFile(request) {
                  calls.push("mount");
                  const release = scoped.mountReadOnlyFile(request);
                  return () => { calls.push("mount-release"); release(); };
                },
                dispose() { calls.push("filesystem-release"); scoped.dispose(); },
              };
            },
            createMaterializer: mountReadOnlyFile => createBrowserTimeZoneMaterializer({
              manifestUrl: `${location.origin}/deploy/deployment.json`,
              platform: { fetch: globalThis.fetch.bind(globalThis), digest, mountReadOnlyFile },
            }),
            async execute({ filesystem }) {
              calls.push("execute");
              const entries = filesystem.preopens.getDirectories();
              if (mode !== "utc") {
                const [[root, path]] = entries;
                if (path !== "/netwasm-timezones") throw new Error("unexpected mount path");
                asset = root.openAt({}, "netwasm-timezones.nwtz", {}, { read: true });
                readBytes = [...asset.read(3n, 0n)[0]];
              } else if (entries.length !== 0) throw new Error("unexpected UTC mount");
              if (mode === "execution-failure") throw new Error("private execution detail");
              return normalExecutionResult(0);
            },
            releaseActions: [{ code: "host.source", message: "Could not release the source filesystem.",
              release() { calls.push("caller-release"); source.dispose(); } }],
          });
          let revoked = asset === undefined;
          if (asset) {
            try { asset.read(1n, 0n); } catch (error) { revoked = error === "bad-descriptor"; }
          }
          results.push({ mode, completion: outcome.completionKind, failure: outcome.primaryFailure?.code ?? null,
            calls, readBytes, revoked });
        }
        return results;
      });
      assert.deepEqual(lifetime, [
        { mode: "selected", completion: "normal", failure: null,
          calls: ["mount", "execute", "mount-release", "filesystem-release", "caller-release"], readBytes: [4, 5, 6], revoked: true },
        { mode: "utc", completion: "normal", failure: null,
          calls: ["execute", "filesystem-release", "caller-release"], readBytes: null, revoked: true },
        { mode: "integrity", completion: "hostFailure", failure: "host.timezone-materialize",
          calls: ["filesystem-release", "caller-release"], readBytes: null, revoked: true },
        { mode: "execution-failure", completion: "hostFailure", failure: "host.deployment-execute",
          calls: ["mount", "execute", "mount-release", "filesystem-release", "caller-release"], readBytes: [4, 5, 6], revoked: true },
      ]);
      assert.equal(sidecarRequests, 3);
      assert.deepEqual(unexpectedRequests, []);
    } finally {
      await browser.close();
    }
  });
}
