// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Immutable;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using NetWasm.Hosting.Build;

namespace NetWasm.Hosting.Build.JavaScript;

public sealed record PageHostEntryRequest(
    string HostingJavaScriptRoot,
    string OutputPath,
    ImmutableArray<BrowserJavaScriptModule> Modules,
    ImmutableArray<string> Arguments,
    ImmutableArray<WorkerEnvironmentVariable> Environment,
    ImmutableArray<string> Clocks,
    string Network,
    bool Randomness,
    ImmutableArray<string> RequiredImportModules,
    bool ValidateDeploymentManifest,
    PageHostPlatform Platform = PageHostPlatform.Browser);

/// <summary>Where a page host runs: a browser page, or Node.js code that imports it.</summary>
public enum PageHostPlatform
{
    Browser,
    Node,
}

public sealed record PageModuleRequest(
    string PageFileName,
    string BuildFingerprint,
    string ManifestSha256,
    string OutputPath,
    string DeclarationPath,
    ImmutableArray<WorkerJavaScriptExport> Exports)
{
    /// <summary>Whether the page host has file mounts (#142) to re-export: it links wasi:filesystem.</summary>
    public bool FileMounts { get; init; }

    /// <summary>
    /// The files a browser page starts downloading as soon as the module loads, or null for Node.js code, which
    /// imports the page bundle statically.
    /// </summary>
    public PageStartupLoad? StartupLoad { get; init; }
}

/// <summary>
/// What a browser page's module downloads in one round trip before the runtime starts: the runtime's module files
/// (preloaded beside the page bundle) and the deployment files the runtime fetches at every startup (prefetched,
/// beside <c>deployment.json</c>), each a URL relative to the module.
/// </summary>
public sealed record PageStartupLoad(ImmutableArray<string> RuntimeModules, ImmutableArray<string> DeploymentFiles);

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
        if (request.Platform == PageHostPlatform.Node)
        {
            // A Node.js host is one self-contained bundle of the hosting sources.
            source.Append("import { createPageSession } from ")
                .Append(BrowserHostSource.ModuleSpecifier(
                    Path.Combine(request.HostingJavaScriptRoot, "page-session-root.mjs"))).AppendLine(";");
            BrowserHostSource.AppendNodeRawImports(source, request.HostingJavaScriptRoot, capabilities,
                request.ValidateDeploymentManifest);
        }
        else
        {
            source.Append("import { createPageSession } from ")
                .Append(JavaScriptRuntime.Quoted(JavaScriptRuntime.Page)).AppendLine(";");
            BrowserHostSource.AppendRawImports(source, JavaScriptRuntime.Page, capabilities);
            BrowserHostSource.AppendManifestValidatorImport(source, request.ValidateDeploymentManifest);
        }
        for (var index = 0; index < modules.Length; index++)
        {
            source.Append("import * as consumer").Append(index).Append(" from ")
                .Append(BrowserHostSource.ModuleSpecifier(modules[index].SourcePath)).AppendLine(";");
        }
        source.AppendLine();
        BrowserHostSource.AppendDeepFreeze(source);
        if (capabilities.LinksFilesystem) BrowserHostSource.AppendFileMounts(source, worker: false);
        source.AppendLine("export const openPage = createPageSession({");
        source.AppendLine("  configuration: Object.freeze({");
        BrowserHostSource.AppendConfiguration(source, "    ", [], request.Arguments, request.Environment,
            request.Clocks, request.Network, request.Randomness);
        source.AppendLine("  }),");
        source.Append("  manifestUrl: new URL(").Append(Json("./" + DeploymentManifestFileName))
            .AppendLine(", import.meta.url).href,");
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
        var page = Json("./" + request.PageFileName);
        var source = new StringBuilder();
        if (request.StartupLoad is null)
        {
            source.Append("import { openPage } from ").Append(page).AppendLine(";");
            if (request.FileMounts)
            {
                source.Append("export { mount, sync, unmount } from ").Append(page).AppendLine(";");
            }
            source.AppendLine();
        }
        source.AppendLine("const startup = Object.freeze({");
        source.Append("  buildFingerprint: ").Append(Json(request.BuildFingerprint)).AppendLine(",");
        source.Append("  manifestSha256: ").Append(Json(request.ManifestSha256)).AppendLine(",");
        source.AppendLine("});");
        source.Append("const exports = Object.freeze(")
            .Append(JsonSerializer.Serialize([.. exports.Select(export => export.Name)], HostingBuildJsonContext.Default.StringArray)).AppendLine(");");
        if (request.StartupLoad is null)
        {
            source.AppendLine("export function open(options) {");
            source.AppendLine("  return openPage(Object.freeze({ exports, options, startup }));");
            source.AppendLine("}");
        }
        else
        {
            AppendStartupLoad(source, "./" + request.PageFileName, request.StartupLoad, request.FileMounts);
        }
        Write(request.OutputPath, source.ToString());
        Write(request.DeclarationPath, Declaration(exports, request.FileMounts));
    }

    // The module imports the page bundle when it loads instead of statically, so its own code runs at once and
    // starts every startup download together: the bundle and the runtime modules it imports (modulepreload links,
    // which a static import chain would discover one level at a time), and the deployment files the runtime fetches
    // next, which open() hands to the runtime to verify and use.
    private static void AppendStartupLoad(StringBuilder source, string page, PageStartupLoad load, bool fileMounts)
    {
        source.Append("const page = import(").Append(Json(page)).AppendLine(");");
        source.AppendLine("page.catch(() => {});");
        source.AppendLine("if (typeof document !== \"undefined\" && document.head !== null) {");
        source.Append("  for (const path of ").Append(JsonArray([page, .. load.RuntimeModules])).AppendLine(") {");
        source.AppendLine("    const link = document.createElement(\"link\");");
        source.AppendLine("    link.rel = \"modulepreload\";");
        source.AppendLine("    link.href = new URL(path, import.meta.url).href;");
        source.AppendLine("    document.head.append(link);");
        source.AppendLine("  }");
        source.AppendLine("}");
        source.AppendLine("const prefetched = new Map();");
        source.Append("for (const path of ").Append(JsonArray([DeploymentManifestFileName, .. load.DeploymentFiles]))
            .AppendLine(") {");
        source.AppendLine("  const url = new URL(path, import.meta.url).href;");
        source.AppendLine("  const response = fetch(url);");
        source.AppendLine("  response.catch(() => {});");
        source.AppendLine("  prefetched.set(url, response);");
        source.AppendLine("}");
        if (fileMounts)
        {
            // Nothing is mounted before the bundle has loaded, since mounting waits for it.
            source.AppendLine("let loaded = null;");
            source.AppendLine("page.then(module => { loaded = module; }, () => {});");
            source.AppendLine("export async function mount(guestPath, source) {");
            source.AppendLine("  return (await page).mount(guestPath, source);");
            source.AppendLine("}");
            source.AppendLine("export async function sync(guestPath) {");
            source.AppendLine("  return (await page).sync(guestPath);");
            source.AppendLine("}");
            source.AppendLine("export function unmount(guestPath) {");
            source.AppendLine("  if (loaded === null) throw new TypeError(`'${guestPath}' is not mounted`);");
            source.AppendLine("  return loaded.unmount(guestPath);");
            source.AppendLine("}");
        }
        source.AppendLine("export function open(options) {");
        source.AppendLine("  return page.then(({ openPage }) => openPage(Object.freeze({ exports, options, prefetched, startup })));");
        source.AppendLine("}");
    }

    private static string Declaration(IEnumerable<WorkerJavaScriptExport> exports, bool fileMounts)
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
        source.AppendLine("export interface NetWasmCallOptions {");
        source.AppendLine("  signal?: AbortSignal;");
        source.AppendLine("}");
        source.AppendLine("export interface NetWasmPageModule {");
        JavaScriptExportSurface.AppendMembers(source, PageJavaScriptExportRealm.Instance, exports);
        source.AppendLine("  close(): Promise<void>;");
        source.AppendLine("  readonly failure: Promise<unknown>;");
        source.AppendLine("}");
        source.AppendLine("export declare function open(options?: NetWasmPageOpenOptions): Promise<NetWasmPageModule>;");
        if (fileMounts)
        {
            source.AppendLine("/** What the user picked: a File or Blob mounts read-only; a handle mounts read-write. */");
            source.AppendLine("export type NetWasmMountSource = Blob | FileSystemFileHandle | FileSystemDirectoryHandle;");
            source.AppendLine("/** Mounts a picked file or directory at an absolute path the program's File APIs see. */");
            source.AppendLine("export declare function mount(guestPath: string, source: NetWasmMountSource): Promise<void>;");
            source.AppendLine("export declare function unmount(guestPath: string): void;");
            source.AppendLine("/** Writes a handle mount's changes back to what the user picked. */");
            source.AppendLine("export declare function sync(guestPath: string): Promise<void>;");
        }
        return source.ToString();
    }

    private static void ValidateHost(PageHostEntryRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateAbsolutePaths(request.HostingJavaScriptRoot, request.OutputPath);
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
            || request.Exports.IsDefaultOrEmpty
            || request.StartupLoad is { } load && (!IsRelativeUrlList(load.RuntimeModules)
                || !IsRelativeUrlList(load.DeploymentFiles)
                || load.DeploymentFiles.Contains(DeploymentManifestFileName, StringComparer.Ordinal)))
        {
            throw new ArgumentException("Page module publication contract is invalid.", nameof(request));
        }
        JavaScriptExportSurface.Validate(PageJavaScriptExportRealm.Instance, request.Exports);
    }

    // Distinct URLs relative to the module: a file name, or ./ or ../ segments and a path.
    private static bool IsRelativeUrlList(ImmutableArray<string> values) =>
        !values.IsDefault
        && values.All(value => RelativeUrlPattern().IsMatch(value))
        && values.Distinct(StringComparer.Ordinal).Count() == values.Length;

    private static void ValidateAbsolutePaths(params string[] paths)
    {
        if (paths.Any(path => string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path)))
        {
            throw new ArgumentException("Page publication paths must be absolute.");
        }
    }

    private const string DeploymentManifestFileName = "deployment.json";

    private static string Json(string value) => JsonSerializer.Serialize(value, HostingBuildJsonContext.Default.String);

    private static string JsonArray(string[] values) =>
        JsonSerializer.Serialize(values, HostingBuildJsonContext.Default.StringArray);

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

    [GeneratedRegex("^(\\./|(\\.\\./)+)?[A-Za-z0-9_][A-Za-z0-9_.-]*(/[A-Za-z0-9_][A-Za-z0-9_.-]*)*$", RegexOptions.CultureInvariant)]
    private static partial Regex RelativeUrlPattern();
}
