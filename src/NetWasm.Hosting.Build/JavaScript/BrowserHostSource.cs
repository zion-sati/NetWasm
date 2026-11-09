using System.Text;
using System.Text.Json;
using NetWasm.Hosting.Execution;
using NetWasm.Hosting.Build;

namespace NetWasm.Hosting.Build.JavaScript;

/// <summary>
/// Source shared by every generated browser host: NetWasm's WASI 0.3 platform and
/// filesystem imports, the platform actions a hosting composition takes, and the
/// MSBuild-selected execution configuration.
/// </summary>
internal static class BrowserHostSource
{
    // A browser grants no host directories: its filesystem is the host's in-memory tree
    // (#142), a read-write root holding /tmp and what the page mounts. Its network is HTTP
    // over fetch; the WASI platform supplies it to an execution the network grant allows.
    private const string MemoryFilesystem =
        "createWasiFilesystem(Object.freeze({ hostFiles: fileMounts.hostFiles, preopens: Object.freeze([...preopens, " +
        "Object.freeze({ access: \"readWrite\", guestPath: \"/\", hostPath: \"/\" })]) }))";

    // A worker reads a picked File in place; the page copies it in when mounted.
    private const string WorkerBlobReader =
        "(blob, start, end) => new Uint8Array(new FileReaderSync().readAsArrayBuffer(blob.slice(start, end)))";

    private const string FetchNetwork =
        "Object.freeze({ modules: createWasiHttpFetch(Object.freeze({ fetch: webPlatform.fetch })) })";

    private static readonly string[] WasiMembers =
    [
        $"createFilesystem: preopens => {MemoryFilesystem},",
        $"createImports: createWasiPlatform(Object.freeze({{ initialDirectory: null, network: {FetchNetwork} }})),",
    ];

    // The hosting module's web actions look their browser APIs up when they
    // run: a browser without one, or a page outside a secure context, still
    // imports the host and reports the failure from open() or run().
    private static readonly string[] WebMembers = ["...webPlatform,"];

    public static void AppendImports(StringBuilder source, string hostingRoot)
    {
        source.Append("import { webPlatform } from ")
            .Append(ModuleSpecifier(Path.Combine(hostingRoot, "browser-web-platform.mjs")))
            .AppendLine(";");
        source.Append("import { createWasiPlatform } from ")
            .Append(ModuleSpecifier(Path.Combine(hostingRoot, "wasi-platform.mjs")))
            .AppendLine(";");
        source.Append("import { createWasiFilesystem } from ")
            .Append(ModuleSpecifier(Path.Combine(hostingRoot, "wasi-filesystem.mjs")))
            .AppendLine(";");
        source.Append("import { createBrowserFileMounts } from ")
            .Append(ModuleSpecifier(Path.Combine(hostingRoot, "browser-file-mounts.mjs")))
            .AppendLine(";");
        source.Append("import { createWasiHttpFetch } from ")
            .Append(ModuleSpecifier(Path.Combine(hostingRoot, "wasi-http-fetch.mjs")))
            .AppendLine(";");
    }

    /// <summary>
    /// The host's in-memory file system (#142), declared before the platform that uses it. A page
    /// host exports its mount, unmount and sync actions; a worker host reads picked files in place.
    /// </summary>
    public static void AppendFileMounts(StringBuilder source, bool worker)
    {
        source.Append("const fileMounts = createBrowserFileMounts(Object.freeze({ readBlobSync: ")
            .Append(worker ? WorkerBlobReader : "null").AppendLine(" }));");
        if (!worker) source.AppendLine("export const { mount, unmount, sync } = fileMounts;");
    }

    /// <summary>The export-session and execution platform: WASI and web actions together.</summary>
    public static void AppendPlatformMembers(StringBuilder source, string indent)
    {
        AppendWasiMembers(source, indent);
        AppendWebMembers(source, indent);
    }

    public static void AppendWasiMembers(StringBuilder source, string indent) =>
        AppendLines(source, indent, WasiMembers);

    public static void AppendWebMembers(StringBuilder source, string indent) =>
        AppendLines(source, indent, WebMembers);

    /// <summary>
    /// Imports for a raw page or worker host from the shared runtime: the WASI
    /// platform, and the filesystem scope when the application links a
    /// filesystem interface.
    /// </summary>
    public static void AppendRawImports(
        StringBuilder source,
        string host,
        RawBrowserHostCapabilities capabilities)
    {
        source.Append("import { createWasiPlatform, webPlatform } from ")
            .Append(JavaScriptRuntime.Quoted(host)).AppendLine(";");
        if (capabilities.LinksFilesystem)
        {
            source.Append("import { createBrowserFileMounts, createWasiFilesystem } from ")
                .Append(JavaScriptRuntime.Quoted(JavaScriptRuntime.Filesystem)).AppendLine(";");
        }
        if (capabilities.LinksHttp)
        {
            source.Append("import { createWasiHttpFetch } from ")
                .Append(JavaScriptRuntime.Quoted(JavaScriptRuntime.Http)).AppendLine(";");
        }
    }

    /// <summary>
    /// <see cref="AppendRawImports"/> and <see cref="AppendManifestValidatorImport"/> for a host
    /// that Node.js code imports: the hosting sources themselves, and Node.js's web actions in
    /// place of the browser's.
    /// </summary>
    public static void AppendNodeRawImports(
        StringBuilder source,
        string hostingRoot,
        RawBrowserHostCapabilities capabilities,
        bool validate)
    {
        void Import(string names, string file) => source.Append("import { ").Append(names).Append(" } from ")
            .Append(ModuleSpecifier(Path.Combine(hostingRoot, file))).AppendLine(";");
        Import("createWasiPlatform", "wasi-platform.mjs");
        Import("nodePlatform as webPlatform", "node-web-platform.mjs");
        if (capabilities.LinksFilesystem)
        {
            Import("createWasiFilesystem", "wasi-filesystem.mjs");
            Import("createBrowserFileMounts", "browser-file-mounts.mjs");
        }
        if (capabilities.LinksHttp) Import("createWasiHttpFetch", "wasi-http-fetch.mjs");
        if (validate) Import("validateDeploymentManifest", "deployment-manifest-validator.mjs");
    }

    /// <summary>The helper <see cref="AppendProviderCatalog"/> freezes the catalog with; written after the imports.</summary>
    /// <summary>Imports the deployment-manifest validator when the entry validates its manifest.</summary>
    public static void AppendManifestValidatorImport(StringBuilder source, bool validate)
    {
        if (validate)
        {
            source.Append("import { validateDeploymentManifest } from ")
                .Append(JavaScriptRuntime.Quoted(JavaScriptRuntime.ManifestValidator)).AppendLine(";");
        }
    }

    /// <summary>
    /// The session's manifest snapshot action. Without validation the entry only
    /// freezes the manifest: the build validated it, and the loader checks its
    /// SHA-256 against the digest the open() module pins.
    /// </summary>
    public static void AppendManifestSnapshot(StringBuilder source, string indent, bool validate) =>
        source.Append(indent).Append("snapshotDeploymentManifest: ")
            .Append(validate ? "validateDeploymentManifest" : "deepFreeze").AppendLine(",");

    public static void AppendDeepFreeze(StringBuilder source)
    {
        source.AppendLine("function deepFreeze(value) {");
        source.AppendLine("  for (const child of Object.values(value)) {");
        source.AppendLine("    if (child !== null && typeof child === \"object\") deepFreeze(child);");
        source.AppendLine("  }");
        source.AppendLine("  return Object.freeze(value);");
        source.AppendLine("}");
    }

    /// <summary>The raw export-session platform: the WASI platform and web actions.</summary>
    public static void AppendRawPlatformMembers(
        StringBuilder source,
        string indent,
        RawBrowserHostCapabilities capabilities)
    {
        AppendLines(source, indent,
        [
            capabilities.LinksFilesystem
                ? $"createFilesystemScope: preopens => {MemoryFilesystem},"
                : "createFilesystemScope: null,",
            $"createImports: createWasiPlatform(Object.freeze({{ initialDirectory: null, network: {(capabilities.LinksHttp ? FetchNetwork : "null")} }})),",
        ]);
        AppendWebMembers(source, indent);
    }

    /// <summary>The platform providers the application's raw bindings require.</summary>
    public static void AppendProviderCatalog(
        StringBuilder source,
        string indent,
        RawBrowserHostCapabilities capabilities) =>
        source.Append(indent).Append("platformProviderCatalog: deepFreeze(")
            .Append(capabilities.ProviderCatalogJson).AppendLine("),");

    public static void AppendConfiguration(
        StringBuilder source,
        string indent,
        IEnumerable<NetWasmApplicationImport> applicationImports,
        IEnumerable<string> arguments,
        IReadOnlyCollection<WorkerEnvironmentVariable> environment,
        IEnumerable<string> clocks,
        string network,
        bool randomness)
    {
        var imports = applicationImports.ToArray();
        if (imports.Length == 0)
        {
            source.Append(indent).AppendLine("applicationImports: Object.freeze([]),");
        }
        else
        {
            source.Append(indent).AppendLine("applicationImports: Object.freeze([");
            foreach (var binding in imports)
            {
                source.Append(indent).Append("  Object.freeze({ module: ").Append(Json(binding.Module))
                    .Append(", artifactPath: ").Append(Json(binding.ArtifactPath))
                    .Append(", sha256: ").Append(Json(binding.Sha256)).AppendLine(" }),");
            }
            source.Append(indent).AppendLine("]),");
        }
        source.Append(indent).Append("arguments: Object.freeze(").Append(Json(arguments)).AppendLine("),");
        source.Append(indent).AppendLine("environment: Object.freeze([");
        foreach (var variable in environment)
        {
            source.Append(indent).Append("  Object.freeze({ name: ").Append(Json(variable.Name))
                .Append(", value: ").Append(Json(variable.Value)).AppendLine(" }),");
        }
        source.Append(indent).AppendLine("]),");
        source.Append(indent).AppendLine("grants: Object.freeze({");
        source.Append(indent).Append("  clocks: Object.freeze(").Append(Json(clocks)).AppendLine("),");
        source.Append(indent).Append("  environment: Object.freeze(")
            .Append(Json(environment.Select(variable => variable.Name))).AppendLine("),");
        source.Append(indent).Append("  network: ").Append(Json(network)).AppendLine(",");
        source.Append(indent).AppendLine("  preopens: Object.freeze([]),");
        source.Append(indent).Append("  randomness: ").Append(randomness ? "true" : "false").AppendLine(",");
        // A browser has no working or temporary directory to grant.
        source.Append(indent).AppendLine("  temporaryDirectory: false,");
        source.Append(indent).AppendLine("  workingDirectory: false,");
        source.Append(indent).AppendLine("}),");
    }

    public static string ModuleSpecifier(string path) =>
        Json(new Uri(Path.GetFullPath(path)).AbsoluteUri);

    private static void AppendLines(StringBuilder source, string indent, IEnumerable<string> lines)
    {
        foreach (var line in lines) source.Append(indent).AppendLine(line);
    }

    private static string Json(string value) => JsonSerializer.Serialize(value, HostingBuildJsonContext.Default.String);

    private static string Json(IEnumerable<string> values) => JsonSerializer.Serialize([.. values], HostingBuildJsonContext.Default.StringArray);
}
