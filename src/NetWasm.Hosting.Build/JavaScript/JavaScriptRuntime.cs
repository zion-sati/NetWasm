using System.Text.Json;

namespace NetWasm.Hosting.Build.JavaScript;

/// <summary>
/// The JavaScript runtime every raw browser host of a release shares: fixed
/// facade modules over the hosting and Preview 2 shim sources. A project
/// bundles all of them into content-hashed files that are the same in every
/// project, so a page and its workers download one copy. An entry imports the
/// modules it uses by their <c>netwasm:runtime/</c> specifiers.
/// </summary>
internal static class JavaScriptRuntime
{
    public const string Page = "page";
    public const string Worker = "worker";
    public const string WorkerClient = "worker-client";
    public const string ManifestValidator = "manifest-validator";
    public const string Filesystem = "filesystem";

    private const string SpecifierPrefix = "netwasm:runtime/";

    // Every raw host links cli and io, so its own module carries them; each
    // other family is a module of its own that only an application using it
    // loads.
    private static readonly string[] HostFamilies = ["cli", "io"];
    private static readonly string[] OptionalFamilies = ["clocks", Filesystem, "http", "random", "sockets"];

    public static string Specifier(string module) => SpecifierPrefix + module;

    public static string Quoted(string module) => JsonSerializer.Serialize(Specifier(module));

    /// <summary>The runtime module a host imports a Preview 2 shim family from.</summary>
    public static string FamilyModule(string family, string host) =>
        HostFamilies.Contains(family, StringComparer.Ordinal) ? host : family;

    /// <summary>
    /// Writes the facades, replacing any other module in the directory. Their
    /// set never depends on the project: the bundler splits shared code by
    /// which facades use it, so a different set would change every file.
    /// </summary>
    public static void WriteFacades(string directory, string hostingRoot, string preview2ShimRoot)
    {
        string Hosting(string file) => BrowserHostSource.ModuleSpecifier(Path.Combine(hostingRoot, file));
        string Shim(string family) =>
            BrowserHostSource.ModuleSpecifier(Path.Combine(preview2ShimRoot, "dist", "browser", family + ".js"));
        string[] HostShim() =>
        [
            $"export {{ createSelectedPreview2ShimFactory }} from {Hosting("preview2-selected-shim.mjs")};",
            .. HostFamilies.Select(family => $"export * as {family} from {Shim(family)};"),
        ];

        var facades = new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            [Page] =
            [
                $"export {{ createPageSession }} from {Hosting("page-session-root.mjs")};",
                .. HostShim(),
            ],
            [Worker] =
            [
                $"export {{ bindWorkerImports }} from {Hosting("worker-import-bindings.mjs")};",
                $"export {{ createBrowserRawExportSession }} from {Hosting("browser-raw-export-session.mjs")};",
                $"export {{ createWorkerSessionHost }} from {Hosting("worker-session-host.mjs")};",
                $"export {{ mergeEnvironment }} from {Hosting("session-options.mjs")};",
                .. HostShim(),
            ],
            [WorkerClient] = [$"export {{ createWorkerSessionClient }} from {Hosting("worker-session-client.mjs")};"],
            [ManifestValidator] =
                [$"export {{ validateDeploymentManifest }} from {Hosting("deployment-manifest-validator.mjs")};"],
        };
        foreach (var family in OptionalFamilies)
        {
            facades[family] = family == Filesystem
                ?
                [
                    $"export * as {family} from {Shim(family)};",
                    $"export {{ createPreview2FilesystemScope }} from {Hosting("preview2-filesystem-scope.mjs")};",
                ]
                : [$"export * as {family} from {Shim(family)};"];
        }

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
