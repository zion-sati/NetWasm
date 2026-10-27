using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using NetWasm.Hosting.Build;

namespace NetWasm.Hosting.Build.JavaScript;

public sealed record WorkerJavaScriptImport(
    string Module,
    string SourcePath,
    ImmutableArray<string> Functions)
{
    // An import module: the final module calls any of its functions directly,
    // so the worker binds every function it exports.
    public bool BindsModule { get; init; }
}

public sealed record WorkerJavaScriptExport(
    string Name,
    ImmutableArray<string> Parameters,
    string Result,
    string? AsyncReturn)
{
    /// <summary>The managed parameter names; generated clients fall back to <c>argument{index}</c>.</summary>
    public ImmutableArray<string> ParameterNames { get; init; } = [];

    /// <summary>Parameters declared non-nullable, which TypeScript types without <c>| null</c>.</summary>
    public ImmutableArray<bool> NonNullableParameters { get; init; } = [];

    /// <summary>Whether the result is declared non-nullable.</summary>
    public bool NonNullableResult { get; init; }

    public bool IsNullableParameter(int index) =>
        index >= NonNullableParameters.Length || !NonNullableParameters[index];
}

public sealed record WorkerEnvironmentVariable(string Name, string Value);

public sealed record WorkerHostEntryRequest(
    string HostingJavaScriptRoot,
    string OutputPath,
    ImmutableArray<WorkerJavaScriptImport> Imports,
    ImmutableArray<string> Arguments,
    ImmutableArray<WorkerEnvironmentVariable> Environment,
    ImmutableArray<string> Clocks,
    string Network,
    bool Randomness,
    ImmutableArray<string> RequiredImportModules,
    bool ValidateDeploymentManifest);

public sealed record WorkerClientEntryRequest(
    string WorkerFileName,
    string BuildFingerprint,
    string ManifestSha256,
    string OutputPath,
    string DeclarationPath,
    ImmutableArray<WorkerJavaScriptExport> Exports);

public interface IWorkerEntryWriter
{
    void WriteHost(WorkerHostEntryRequest request);
    void WriteClient(WorkerClientEntryRequest request);
}

public sealed partial class WorkerEntryWriter : IWorkerEntryWriter
{
    public void WriteHost(WorkerHostEntryRequest request)
    {
        ValidateHost(request);
        var imports = request.Imports.OrderBy(binding => binding.Module, StringComparer.Ordinal).ToArray();
        var capabilities = RawBrowserHostCapabilities.Select(request.HostingJavaScriptRoot, request.RequiredImportModules);
        var source = new StringBuilder();
        AppendLine(source, $"import {{ bindWorkerImports, createBrowserRawExportSession, createWorkerSessionHost, mergeEnvironment }} from {JavaScriptRuntime.Quoted(JavaScriptRuntime.Worker)};");
        BrowserHostSource.AppendRawImports(source, JavaScriptRuntime.Worker, capabilities);
        BrowserHostSource.AppendManifestValidatorImport(source, request.ValidateDeploymentManifest);
        for (var index = 0; index < imports.Length; index++)
        {
            AppendLine(source, $"import * as consumer{index} from {ModuleSpecifier(imports[index].SourcePath)};");
        }
        source.AppendLine();
        BrowserHostSource.AppendDeepFreeze(source);
        source.AppendLine("const manifestUrl = new URL(\"./deployment.json\", import.meta.url).href;");
        source.AppendLine("let fatalScheduled = false;");
        source.AppendLine("function fatal(cause) {");
        source.AppendLine("  if (fatalScheduled) return;");
        source.AppendLine("  fatalScheduled = true;");
        source.AppendLine("  const error = cause instanceof Error ? cause : new Error(\"NetWasm worker failed\");");
        source.AppendLine("  queueMicrotask(() => { throw error; });");
        source.AppendLine("  globalThis.close();");
        source.AppendLine("}");
        source.AppendLine("function createConsumerModules(notify) {");
        for (var index = 0; index < imports.Length; index++)
        {
            AppendLine(source, $"const imports{index} = bindWorkerImports(consumer{index}, notify);");
        }
        source.AppendLine("return Object.freeze({");
        for (var index = 0; index < imports.Length; index++)
        {
            var binding = imports[index];
            AppendLine(source, $"  {Json(binding.Module)}: Object.freeze({{");
            if (binding.BindsModule)
            {
                AppendLine(source, $"    ...Object.fromEntries(Object.entries(imports{index}).filter(([name, value]) => name !== \"createWorkerImports\" && typeof value === \"function\")),");
            }
            foreach (var function in binding.Functions.Order(StringComparer.Ordinal))
            {
                AppendLine(source, $"    {Json(function)}: imports{index}[{Json(function)}],");
            }
            source.AppendLine("  }),");
        }
        source.AppendLine("}); }");
        source.AppendLine("const configuration = Object.freeze({");
        BrowserHostSource.AppendConfiguration(source, "  ", [], request.Arguments, request.Environment,
            request.Clocks, request.Network, request.Randomness);
        source.AppendLine("});");
        source.AppendLine("function open(request) { const openSession = createBrowserRawExportSession({");
        source.AppendLine("  configuration: mergeEnvironment(configuration, request.environment),");
        source.AppendLine("  consumerModules: createConsumerModules(request.notify),");
        source.AppendLine("  manifestUrl,");
        source.AppendLine("  platform: Object.freeze({");
        BrowserHostSource.AppendRawPlatformMembers(source, "    ", capabilities);
        source.AppendLine("  }),");
        BrowserHostSource.AppendProviderCatalog(source, "  ", capabilities);
        BrowserHostSource.AppendManifestSnapshot(source, "  ", request.ValidateDeploymentManifest);
        source.AppendLine("  stderr: request.stderr,");
        source.AppendLine("  stdout: request.stdout,");
        source.AppendLine("});");
        source.AppendLine("return openSession({ startup: request.startup }); }");
        source.AppendLine("const host = createWorkerSessionHost({");
        source.AppendLine("  openSession: open,");
        source.AppendLine("  post: (message, transfer = []) => globalThis.postMessage(message, transfer),");
        source.AppendLine("  terminate: cause => cause === null ? globalThis.close() : fatal(cause),");
        source.AppendLine("});");
        source.AppendLine("globalThis.addEventListener(\"message\", event => { host.receive(event.data).catch(fatal); });");
        source.AppendLine("globalThis.addEventListener(\"messageerror\", () => fatal(new Error(\"Worker message could not be decoded\")));");
        Write(request.OutputPath, source.ToString());
    }

    public void WriteClient(WorkerClientEntryRequest request)
    {
        ValidateClient(request);
        var exports = request.Exports.OrderBy(export => export.Name, StringComparer.Ordinal).ToArray();
        var operations = JsonSerializer.Serialize([.. exports.Select(export => export.Name)], HostingBuildJsonContext.Default.StringArray);
        var source = new StringBuilder();
        AppendLine(source, $"import {{ createWorkerSessionClient }} from {JavaScriptRuntime.Quoted(JavaScriptRuntime.WorkerClient)};");
        source.AppendLine();
        AppendLine(source, $"const operations = Object.freeze({operations});");
        source.AppendLine("export async function createWorker(options = {}) {");
        source.AppendLine("  const onNotification = options.onNotification ?? null;");
        source.AppendLine("  if (onNotification !== null && typeof onNotification !== \"function\") throw new TypeError(\"worker notification handler must be a function\");");
        AppendLine(source, $"  const worker = new Worker(new URL({Json("./" + request.WorkerFileName)}, import.meta.url), {{ type: \"module\" }});");
        source.AppendLine("  const session = createWorkerSessionClient({");
        source.AppendLine("    generation: crypto.randomUUID(),");
        source.AppendLine("    operations, onNotification,");
        source.AppendLine("    environment: options.environment, stderr: options.stderr, stdout: options.stdout,");
        source.AppendLine("    startup: Object.freeze({");
        AppendLine(source, $"      buildFingerprint: {Json(request.BuildFingerprint)},");
        AppendLine(source, $"      manifestSha256: {Json(request.ManifestSha256)},");
        source.AppendLine("    }),");
        source.AppendLine("    worker,");
        source.AppendLine("  });");
        source.AppendLine("  await session.ready;");
        source.AppendLine("  return Object.freeze({");
        foreach (var export in exports)
        {
            var arguments = string.Join(", ", WorkerJavaScriptExportRealm.Instance.ParameterNames(export));
            var parameters = string.IsNullOrEmpty(arguments)
                ? "options"
                : $"{arguments}, options";
            AppendLine(source,
                $"    {Json(export.Name)}: ({parameters}) => session.invoke({Json(export.Name)}, [{arguments}], options),");
        }
        source.AppendLine("    dispose: () => session.dispose(),");
        source.AppendLine("    terminate: () => session.terminate(),");
        source.AppendLine("  });");
        source.AppendLine("}");
        Write(request.OutputPath, source.ToString());
        Write(request.DeclarationPath, Declaration(exports));
    }

    private static string Declaration(IEnumerable<WorkerJavaScriptExport> exports)
    {
        var source = new StringBuilder();
        source.AppendLine("export interface NetWasmWorkerCallOptions {");
        source.AppendLine("  signal?: AbortSignal;");
        source.AppendLine("}");
        source.AppendLine("export interface NetWasmWorkerClient {");
        JavaScriptExportSurface.AppendMembers(source, WorkerJavaScriptExportRealm.Instance, exports);
        source.AppendLine("  dispose(): Promise<void>;");
        source.AppendLine("  terminate(): void;");
        source.AppendLine("}");
        source.AppendLine("export interface NetWasmWorkerStartupOptions { environment?: Record<string, string>; stdout?: (line: string) => void; stderr?: (line: string) => void; onNotification?: (value: { operation: string; arguments: unknown[] }) => void | Promise<void>; }");
        source.AppendLine("export declare function createWorker(options?: NetWasmWorkerStartupOptions): Promise<NetWasmWorkerClient>;");
        return source.ToString();
    }

    private static void ValidateHost(WorkerHostEntryRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateAbsolutePaths(request.HostingJavaScriptRoot, request.OutputPath);
        if (request.Imports.IsDefault || request.Arguments.IsDefault || request.Environment.IsDefault
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
            throw new ArgumentException("Worker host configuration is invalid.", nameof(request));
        }
        var modules = new HashSet<string>(StringComparer.Ordinal);
        foreach (var binding in request.Imports)
        {
            ArgumentNullException.ThrowIfNull(binding);
            ValidateApplicationModule(binding.Module);
            ValidateAbsolutePaths(binding.SourcePath);
            if (!modules.Add(binding.Module) || binding.Functions.IsDefault
                || binding.Functions.IsEmpty && !binding.BindsModule
                || binding.Functions.Any(string.IsNullOrWhiteSpace)
                || binding.Functions.Distinct(StringComparer.Ordinal).Count() != binding.Functions.Length)
            {
                throw new ArgumentException("Worker JavaScript imports are invalid.", nameof(request));
            }
        }
    }

    private static void ValidateClient(WorkerClientEntryRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateAbsolutePaths(request.OutputPath, request.DeclarationPath);
        if (Path.GetFileName(request.WorkerFileName) != request.WorkerFileName
            || string.IsNullOrWhiteSpace(request.WorkerFileName)
            || !DigestPattern().IsMatch(request.BuildFingerprint)
            || !DigestPattern().IsMatch(request.ManifestSha256)
            || request.Exports.IsDefaultOrEmpty)
        {
            throw new ArgumentException("Worker client publication contract is invalid.", nameof(request));
        }
        JavaScriptExportSurface.Validate(WorkerJavaScriptExportRealm.Instance, request.Exports);
    }

    private static void ValidateApplicationModule(string module)
    {
        if (string.IsNullOrWhiteSpace(module) || module == "netwasm.host.v1"
            || module.StartsWith("wasi:", StringComparison.Ordinal)
            || module.StartsWith("netwasm:platform/", StringComparison.Ordinal))
        {
            throw new ArgumentException("Worker JavaScript import module is reserved.");
        }
    }

    private static void ValidateAbsolutePaths(params string[] paths)
    {
        if (paths.Any(path => string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path)))
        {
            throw new ArgumentException("Worker publication paths must be absolute.");
        }
    }

    private static string ModuleSpecifier(string path) => BrowserHostSource.ModuleSpecifier(path);

    private static string Json(string value) => JsonSerializer.Serialize(value, HostingBuildJsonContext.Default.String);


    private static void AppendLine(StringBuilder builder, FormattableString value) =>
        builder.AppendLine(value.ToString(CultureInfo.InvariantCulture));

    private static void Write(string path, string source)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(
            path,
            source.ReplaceLineEndings("\n") + (source.EndsWith('\n') ? string.Empty : "\n"),
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    [GeneratedRegex("^[0-9a-f]{64}$", RegexOptions.CultureInvariant)]
    private static partial Regex DigestPattern();
}
