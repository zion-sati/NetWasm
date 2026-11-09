using System.Text.Json;
using NetWasm.Hosting.Build;

namespace NetWasm.Hosting.Build.JavaScript;

/// <summary>
/// The JavaScript runtime every raw browser host of a release shares: fixed
/// facade modules over the hosting sources. A project bundles all of them into
/// content-hashed files that are the same in every project, so a page and its
/// workers download one copy. An entry imports the modules it uses by their
/// <c>netwasm:runtime/</c> specifiers.
/// </summary>
internal static class JavaScriptRuntime
{
    public const string Page = "page";
    public const string Worker = "worker";
    public const string WorkerClient = "worker-client";
    public const string ManifestValidator = "manifest-validator";
    public const string Filesystem = "filesystem";
    public const string Http = "http";

    private const string SpecifierPrefix = "netwasm:runtime/";

    public static string Specifier(string module) => SpecifierPrefix + module;

    public static string Quoted(string module) => JsonSerializer.Serialize(Specifier(module), HostingBuildJsonContext.Default.String);

    /// <summary>
    /// Writes the facades, replacing any other module in the directory. Their
    /// set never depends on the project: the bundler splits shared code by
    /// which facades use it, so a different set would change every file.
    /// </summary>
    public static void WriteFacades(string directory, string hostingRoot)
    {
        string Hosting(string file) => BrowserHostSource.ModuleSpecifier(Path.Combine(hostingRoot, file));
        string[] HostPlatform() =>
        [
            $"export {{ createWasiPlatform }} from {Hosting("wasi-platform.mjs")};",
            $"export {{ webPlatform }} from {Hosting("browser-web-platform.mjs")};",
        ];

        var facades = new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            [Page] =
            [
                $"export {{ createPageSession }} from {Hosting("page-session-root.mjs")};",
                .. HostPlatform(),
            ],
            [Worker] =
            [
                $"export {{ bindWorkerImports }} from {Hosting("worker-import-bindings.mjs")};",
                $"export {{ createBrowserRawExportSession }} from {Hosting("browser-raw-export-session.mjs")};",
                $"export {{ createWorkerSessionHost }} from {Hosting("worker-session-host.mjs")};",
                $"export {{ mergeEnvironment }} from {Hosting("session-options.mjs")};",
                .. HostPlatform(),
            ],
            [WorkerClient] =
            [
                $"export {{ createWorkerSessionClient }} from {Hosting("worker-session-client.mjs")};",
                $"export {{ createWorkerFileMounts }} from {Hosting("worker-file-mounts.mjs")};",
            ],
            [ManifestValidator] =
                [$"export {{ validateDeploymentManifest }} from {Hosting("deployment-manifest-validator.mjs")};"],
            [Filesystem] =
            [
                $"export {{ createWasiFilesystem }} from {Hosting("wasi-filesystem.mjs")};",
                $"export {{ createBrowserFileMounts }} from {Hosting("browser-file-mounts.mjs")};",
            ],
            [Http] = [$"export {{ createWasiHttpFetch }} from {Hosting("wasi-http-fetch.mjs")};"],
        };

        Directory.CreateDirectory(directory);
        foreach (var stale in Directory.EnumerateFiles(directory, "*.mjs")
                     .Where(path => !facades.ContainsKey(Path.GetFileNameWithoutExtension(path))))
        {
            File.Delete(stale);
        }
        foreach (var (name, lines) in facades)
        {
            File.WriteAllText(Path.Combine(directory, name + ".mjs"), string.Join('\n', lines) + "\n");
        }
    }
}
