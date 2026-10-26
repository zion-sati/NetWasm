using System.Collections.Immutable;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace NetWasm.Hosting.Build.JavaScript;

public sealed record PageHostEntryRequest(
    string HostingJavaScriptRoot,
    string Preview2ShimRoot,
    string OutputPath,
    ImmutableArray<BrowserJavaScriptModule> Modules,
    ImmutableArray<string> Arguments,
    ImmutableArray<WorkerEnvironmentVariable> Environment,
    ImmutableArray<string> Clocks,
    string Network,
    bool Randomness,
    ImmutableArray<string> RequiredImportModules,
    bool ValidateDeploymentManifest);

public sealed record PageModuleRequest(
    string PageFileName,
    string BuildFingerprint,
    string ManifestSha256,
    string OutputPath,
    string DeclarationPath,
    ImmutableArray<WorkerJavaScriptExport> Exports);

public interface IPageEntryWriter
{
    /// <summary>Writes the page host entry that is bundled into the deployment.</summary>
    void WriteHost(PageHostEntryRequest request);

    /// <summary>Writes the typed <c>open()</c> module that pins one published deployment.</summary>
    void WriteModule(PageModuleRequest request);
}

public sealed partial class PageEntryWriter : IPageEntryWriter
{
    public void WriteHost(PageHostEntryRequest request)
    {
        ValidateHost(request);
        var modules = request.Modules.OrderBy(module => module.Module, StringComparer.Ordinal).ToArray();
        var capabilities = RawBrowserHostCapabilities.Select(request.HostingJavaScriptRoot, request.RequiredImportModules);
        var source = new StringBuilder();
        source.Append("import { createPageSession } from ")
            .Append(BrowserHostSource.ModuleSpecifier(Path.Combine(request.HostingJavaScriptRoot, "page-session-root.mjs")))
            .AppendLine(";");
        BrowserHostSource.AppendRawImports(source, request.HostingJavaScriptRoot, request.Preview2ShimRoot, capabilities);
        BrowserHostSource.AppendManifestValidatorImport(source, request.HostingJavaScriptRoot, request.ValidateDeploymentManifest);
        for (var index = 0; index < modules.Length; index++)
        {
            source.Append("import * as consumer").Append(index).Append(" from ")
                .Append(BrowserHostSource.ModuleSpecifier(modules[index].SourcePath)).AppendLine(";");
        }
        source.AppendLine();
        BrowserHostSource.AppendDeepFreeze(source);
        source.AppendLine("export const openPage = createPageSession({");
        source.AppendLine("  configuration: Object.freeze({");
        BrowserHostSource.AppendConfiguration(source, "    ", [], request.Arguments, request.Environment,
            request.Clocks, request.Network, request.Randomness);
        source.AppendLine("  }),");
        source.AppendLine("  manifestUrl: new URL(\"./deployment.json\", import.meta.url).href,");
        source.AppendLine("  modules: Object.freeze({");
        for (var index = 0; index < modules.Length; index++)
        {
            source.Append("    ").Append(Json(modules[index].Module)).Append(": consumer").Append(index).AppendLine(",");
        }
        source.AppendLine("  }),");
        source.AppendLine("  platform: Object.freeze({");
        BrowserHostSource.AppendRawPlatformMembers(source, "    ", capabilities);
        source.AppendLine("  }),");
        BrowserHostSource.AppendProviderCatalog(source, "  ", capabilities);
        BrowserHostSource.AppendManifestSnapshot(source, "  ", request.ValidateDeploymentManifest);
        source.AppendLine("});");
        Write(request.OutputPath, source.ToString());
    }

    public void WriteModule(PageModuleRequest request)
    {
        ValidateModule(request);
        var exports = request.Exports.OrderBy(export => export.Name, StringComparer.Ordinal).ToArray();
        var source = new StringBuilder();
        source.Append("import { openPage } from ").Append(Json("./" + request.PageFileName)).AppendLine(";");
        source.AppendLine();
        source.AppendLine("const startup = Object.freeze({");
        source.Append("  buildFingerprint: ").Append(Json(request.BuildFingerprint)).AppendLine(",");
        source.Append("  manifestSha256: ").Append(Json(request.ManifestSha256)).AppendLine(",");
        source.AppendLine("});");
        source.Append("const exports = Object.freeze(")
            .Append(JsonSerializer.Serialize(exports.Select(export => export.Name))).AppendLine(");");
        source.AppendLine("export function open(options) {");
        source.AppendLine("  return openPage(Object.freeze({ exports, options, startup }));");
        source.AppendLine("}");
        Write(request.OutputPath, source.ToString());
        Write(request.DeclarationPath, Declaration(exports));
    }

    private static string Declaration(IEnumerable<WorkerJavaScriptExport> exports)
    {
        var source = new StringBuilder();
        source.AppendLine("export interface NetWasmPagePlatformHooks {");
        source.AppendLine("  now?: () => number;");
        source.AppendLine("  random?: (bytes: Uint8Array) => void;");
        source.AppendLine("}");
        source.AppendLine("export interface NetWasmPageOpenOptions {");
        source.AppendLine("  environment?: Record<string, string>;");
        source.AppendLine("  modules?: Record<string, object>;");
        source.AppendLine("  onNotification?: (value: { operation: string; arguments: unknown[] }) => void | Promise<void>;");
        source.AppendLine("  platform?: NetWasmPagePlatformHooks;");
        source.AppendLine("  stderr?: (line: string) => void;");
        source.AppendLine("  stdout?: (line: string) => void;");
        source.AppendLine("}");
        source.AppendLine("export interface NetWasmPageModule {");
        JavaScriptExportSurface.AppendMembers(source, PageJavaScriptExportRealm.Instance, exports);
        source.AppendLine("  close(): Promise<void>;");
        source.AppendLine("  readonly failure: Promise<unknown>;");
        source.AppendLine("}");
        source.AppendLine("export declare function open(options?: NetWasmPageOpenOptions): Promise<NetWasmPageModule>;");
        return source.ToString();
    }

    private static void ValidateHost(PageHostEntryRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateAbsolutePaths(request.HostingJavaScriptRoot, request.Preview2ShimRoot, request.OutputPath);
        if (request.Modules.IsDefault || request.Arguments.IsDefault || request.Environment.IsDefault
            || request.RequiredImportModules.IsDefault
            || request.Clocks.IsDefault || request.Network is not ("allowAll" or "denyAll")
            || request.Arguments.Any(argument => argument.Contains('\0'))
            || request.Environment.Any(variable => string.IsNullOrWhiteSpace(variable.Name)
                || variable.Name.Contains('\0') || variable.Value.Contains('\0'))
            || request.Environment.Select(variable => variable.Name).Distinct(StringComparer.Ordinal).Count()
                != request.Environment.Length
            || request.Clocks.Any(clock => clock is not ("wall" or "monotonic"))
            || request.Clocks.Distinct(StringComparer.Ordinal).Count() != request.Clocks.Length)
        {
            throw new ArgumentException("Page host configuration is invalid.", nameof(request));
        }
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var module in request.Modules)
        {
            ArgumentNullException.ThrowIfNull(module);
            if (string.IsNullOrWhiteSpace(module.Module) || module.Module == "netwasm.host.v1"
                || module.Module.StartsWith("wasi:", StringComparison.Ordinal)
                || module.Module.StartsWith("netwasm:platform/", StringComparison.Ordinal)
                || !names.Add(module.Module))
            {
                throw new ArgumentException($"Page JavaScript module '{module.Module}' is reserved or repeated.", nameof(request));
            }
            ValidateAbsolutePaths(module.SourcePath);
        }
    }

    private static void ValidateModule(PageModuleRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateAbsolutePaths(request.OutputPath, request.DeclarationPath);
        if (string.IsNullOrWhiteSpace(request.PageFileName)
            || Path.GetFileName(request.PageFileName) != request.PageFileName
            || !DigestPattern().IsMatch(request.BuildFingerprint)
            || !DigestPattern().IsMatch(request.ManifestSha256)
            || request.Exports.IsDefaultOrEmpty)
        {
            throw new ArgumentException("Page module publication contract is invalid.", nameof(request));
        }
        JavaScriptExportSurface.Validate(PageJavaScriptExportRealm.Instance, request.Exports);
    }

    private static void ValidateAbsolutePaths(params string[] paths)
    {
        if (paths.Any(path => string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path)))
        {
            throw new ArgumentException("Page publication paths must be absolute.");
        }
    }

    private static string Json(string value) => JsonSerializer.Serialize(value);

    private static void Write(string path, string source)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(
            path,
            source.ReplaceLineEndings("\n"),
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    [GeneratedRegex("^[0-9a-f]{64}$", RegexOptions.CultureInvariant)]
    private static partial Regex DigestPattern();
}
