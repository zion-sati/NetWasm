using System.Text;
using System.Text.Json;

namespace NetWasm.Hosting.Build.JavaScript;

public sealed record BrowserBootstrapEntryRequest(
    string HostingBrowserModulePath,
    string Preview2ShimRoot,
    string OutputPath);

public interface IBrowserBootstrapEntryWriter
{
    void Write(BrowserBootstrapEntryRequest request);
}

public sealed class BrowserBootstrapEntryWriter : IBrowserBootstrapEntryWriter
{
    public void Write(BrowserBootstrapEntryRequest request)
    {
        Validate(request);
        var hosting = ModuleSpecifier(request.HostingBrowserModulePath);
        var instantiation = ModuleSpecifier(Path.Combine(
            request.Preview2ShimRoot,
            "dist",
            "common",
            "instantiation.js"));
        var filesystem = ModuleSpecifier(Path.Combine(
            request.Preview2ShimRoot,
            "dist",
            "browser",
            "filesystem.js"));
        var source = $$"""
            import { createBrowserNetWasmBootstrap } from {{hosting}};
            import { WASIShim } from {{instantiation}};
            import { createFilesystem, InMemoryFilesystemAdapter } from {{filesystem}};

            const manifestUrl = new URL("./deployment.json", import.meta.url).href;
            const preview2 = Object.freeze({
              createFilesystem({ preopens }) {
                if (Object.keys(preopens).length !== 0) {
                  throw new TypeError("browser preopens require explicit browser capabilities");
                }
                return createFilesystem({
                  adapter: new InMemoryFilesystemAdapter(),
                  preopens: Object.freeze({}),
                });
              },
              createShim: config => new WASIShim(config),
            });
            const web = Object.freeze({
              compileCoreModule: WebAssembly.compile.bind(WebAssembly),
              createModuleUrl: bytes => URL.createObjectURL(new Blob(
                [bytes],
                { type: "text/javascript" })),
              digest: crypto.subtle.digest.bind(crypto.subtle),
              fetch: globalThis.fetch.bind(globalThis),
              importModule: url => import(url),
              revokeModuleUrl: URL.revokeObjectURL.bind(URL),
            });

            export const executeNetWasm = createBrowserNetWasmBootstrap({
              manifestUrl,
              preview2,
              web,
            });
            """;
        Directory.CreateDirectory(Path.GetDirectoryName(request.OutputPath)!);
        File.WriteAllText(
            request.OutputPath,
            source.ReplaceLineEndings("\n") + "\n",
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    private static string ModuleSpecifier(string path) =>
        JsonSerializer.Serialize(new Uri(Path.GetFullPath(path)).AbsoluteUri);

    private static void Validate(BrowserBootstrapEntryRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        foreach (var path in new[]
                 {
                     request.HostingBrowserModulePath,
                     request.Preview2ShimRoot,
                     request.OutputPath,
                 })
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(path);
            if (!Path.IsPathFullyQualified(path))
            {
                throw new ArgumentException(
                    "Browser bootstrap paths must be absolute.",
                    nameof(request));
            }
        }
    }
}
