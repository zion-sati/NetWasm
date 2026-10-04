using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace NetWasm.Hosting.Build.JavaScript;

public sealed record WorkerJavaScriptImport(
    string Module,
    string SourcePath,
    ImmutableArray<string> Functions);

public sealed record WorkerJavaScriptExport(
    string Name,
    ImmutableArray<string> Parameters,
    string Result,
    string? AsyncReturn);

public sealed record WorkerEnvironmentVariable(string Name, string Value);

public sealed record WorkerHostEntryRequest(
    string HostingJavaScriptRoot,
    string Preview2ShimRoot,
    string OutputPath,
    ImmutableArray<WorkerJavaScriptImport> Imports,
    ImmutableArray<string> Arguments,
    ImmutableArray<WorkerEnvironmentVariable> Environment,
    ImmutableArray<string> Clocks,
    string Network,
    bool Randomness);

public sealed record WorkerClientEntryRequest(
    string HostingJavaScriptRoot,
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
    private static readonly HashSet<string> ReservedExportNames =
        new(StringComparer.Ordinal)
        {
            "__proto__",
            "constructor",
            "dispose",
            "prototype",
            "terminate",
            "then",
        };

    public void WriteHost(WorkerHostEntryRequest request)
    {
        ValidateHost(request);
        var imports = request.Imports.OrderBy(binding => binding.Module, StringComparer.Ordinal).ToArray();
        var source = new StringBuilder();
        AppendLine(source, $"import {{ bindWorkerImports }} from {ModuleSpecifier(Path.Combine(request.HostingJavaScriptRoot, "worker-import-bindings.mjs"))};");
        AppendLine(source, $"import {{ createBrowserRawExportSession }} from {ModuleSpecifier(Path.Combine(request.HostingJavaScriptRoot, "browser-raw-export-session.mjs"))};");
        AppendLine(source, $"import {{ createWorkerSessionHost }} from {ModuleSpecifier(Path.Combine(request.HostingJavaScriptRoot, "worker-session-host.mjs"))};");
        AppendLine(source, $"import {{ WASIShim }} from {ModuleSpecifier(Path.Combine(request.Preview2ShimRoot, "dist", "common", "instantiation.js"))};");
        AppendLine(source, $"import {{ createFilesystem, InMemoryFilesystemAdapter }} from {ModuleSpecifier(Path.Combine(request.Preview2ShimRoot, "dist", "browser", "filesystem.js"))};");
        for (var index = 0; index < imports.Length; index++)
        {
            AppendLine(source, $"import * as consumer{index} from {ModuleSpecifier(imports[index].SourcePath)};");
        }
        source.AppendLine();
        source.AppendLine("const manifestUrl = new URL(\"./deployment.json\", import.meta.url).href;");
        source.AppendLine("let fatalScheduled = false;");
        source.AppendLine("function fatal(cause) {");
        source.AppendLine("  if (fatalScheduled) return;");
        source.AppendLine("  fatalScheduled = true;");
        source.AppendLine("  const error = cause instanceof Error ? cause : new Error(\"NetWasm worker failed\");");
        source.AppendLine("  queueMicrotask(() => { throw error; });");
        source.AppendLine("  globalThis.close();");
        source.AppendLine("}");
        source.AppendLine("const output = name => Object.freeze({");
        source.AppendLine("  write(bytes) {");
        source.AppendLine("    const text = new TextDecoder().decode(bytes);");
        source.AppendLine("    console[name](text.endsWith(\"\\n\") ? text.slice(0, -1) : text);");
        source.AppendLine("  },");
        source.AppendLine("});");
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
            foreach (var function in binding.Functions.Order(StringComparer.Ordinal))
            {
                AppendLine(source, $"    {Json(function)}: imports{index}[{Json(function)}],");
            }
            source.AppendLine("  }),");
        }
        source.AppendLine("}); }");
        source.AppendLine("function open(request) { const openSession = createBrowserRawExportSession({");
        source.AppendLine("  configuration: Object.freeze({");
        source.AppendLine("    applicationImports: Object.freeze([]),");
        AppendLine(source, $"    arguments: Object.freeze({Json(request.Arguments)}),");
        source.AppendLine("    environment: Object.freeze([");
        foreach (var variable in request.Environment)
        {
            AppendLine(source, $"      Object.freeze({{ name: {Json(variable.Name)}, value: {Json(variable.Value)} }}),");
        }
        source.AppendLine("    ]),");
        source.AppendLine("    grants: Object.freeze({");
        AppendLine(source, $"      clocks: Object.freeze({Json(request.Clocks)}),");
        AppendLine(source, $"      environment: Object.freeze({Json(request.Environment.Select(variable => variable.Name))}),");
        AppendLine(source, $"      network: {Json(request.Network)},");
        source.AppendLine("      preopens: Object.freeze([]),");
        AppendLine(source, $"      randomness: {request.Randomness.ToString().ToLowerInvariant()},");
        source.AppendLine("    }),");
        source.AppendLine("  }),");
        source.AppendLine("  consumerModules: createConsumerModules(request.notify),");
        source.AppendLine("  manifestUrl,");
        source.AppendLine("  platform: Object.freeze({");
        source.AppendLine("    compileCoreModule: WebAssembly.compile.bind(WebAssembly),");
        source.AppendLine("    createFilesystem({ preopens }) {");
        source.AppendLine("      if (Object.keys(preopens).length !== 0) throw new TypeError(\"browser preopens require explicit capabilities\");");
        source.AppendLine("      return createFilesystem({ adapter: new InMemoryFilesystemAdapter(), preopens: Object.freeze({}) });");
        source.AppendLine("    },");
        source.AppendLine("    createModuleUrl: bytes => URL.createObjectURL(new Blob([bytes], { type: \"text/javascript\" })),");
        source.AppendLine("    createShim: configuration => new WASIShim(configuration),");
        source.AppendLine("    digest: crypto.subtle.digest.bind(crypto.subtle),");
        source.AppendLine("    fetch: globalThis.fetch.bind(globalThis),");
        source.AppendLine("    importModule: url => import(url),");
        source.AppendLine("    revokeModuleUrl: URL.revokeObjectURL.bind(URL),");
        source.AppendLine("  }),");
        source.AppendLine("  stderr: output(\"error\"),");
        source.AppendLine("  stdout: output(\"log\"),");
        source.AppendLine("});");
        source.AppendLine("return openSession({ startup: request.startup }); }");
        source.AppendLine("const host = createWorkerSessionHost({");
        source.AppendLine("  openSession: open,");
        source.AppendLine("  post: message => globalThis.postMessage(message),");
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
        var operations = JsonSerializer.Serialize(exports.Select(export => export.Name));
        var source = new StringBuilder();
        AppendLine(source, $"import {{ createWorkerSessionClient }} from {ModuleSpecifier(Path.Combine(request.HostingJavaScriptRoot, "worker-session-client.mjs"))};");
        source.AppendLine();
        AppendLine(source, $"const operations = Object.freeze({operations});");
        source.AppendLine("export async function createWorker(options = {}) {");
        source.AppendLine("  const onNotification = options.onNotification ?? null;");
        source.AppendLine("  if (onNotification !== null && typeof onNotification !== \"function\") throw new TypeError(\"worker notification handler must be a function\");");
        AppendLine(source, $"  const worker = new Worker(new URL({Json("./" + request.WorkerFileName)}, import.meta.url), {{ type: \"module\" }});");
        source.AppendLine("  const session = createWorkerSessionClient({");
        source.AppendLine("    generation: crypto.randomUUID(),");
        source.AppendLine("    operations, onNotification,");
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
            var arguments = string.Join(", ", Enumerable.Range(0, export.Parameters.Length)
                .Select(index => $"argument{index}"));
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
        source.AppendLine("  timeoutMilliseconds?: number;");
        source.AppendLine("}");
        source.AppendLine("export interface NetWasmWorkerClient {");
        foreach (var export in exports)
        {
            var values = export.Parameters.Select(
                (type, index) => $"argument{index}: {TypeScriptType(type, allowVoid: false)}");
            var parameters = string.Join(", ", values.Append("options?: NetWasmWorkerCallOptions"));
            AppendLine(source, $"  {Json(export.Name)}({parameters}): Promise<{TypeScriptType(export.Result, allowVoid: true)}>;");
        }
        source.AppendLine("  dispose(): Promise<void>;");
        source.AppendLine("  terminate(): void;");
        source.AppendLine("}");
        source.AppendLine("export interface NetWasmWorkerStartupOptions { onNotification?: (value: { operation: string; arguments: unknown[] }) => void | Promise<void>; }");
        source.AppendLine("export declare function createWorker(options?: NetWasmWorkerStartupOptions): Promise<NetWasmWorkerClient>;");
        return source.ToString();
    }

    private static string TypeScriptType(string value, bool allowVoid) => value switch
    {
        "void" when allowVoid => "void",
        "bool" => "boolean",
        "char" => "string",
        "string" => "string | null",
        "i64" or "u64" => "bigint",
        "bytes" => "Uint8Array | null",
        "i8" or "u8" or "i16" or "u16" or "i32" or "u32" or "f32" or "f64" => "number",
        _ => throw new ArgumentException($"Worker JavaScript ABI type '{value}' is unsupported."),
    };

    private static void ValidateHost(WorkerHostEntryRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateAbsolutePaths(request.HostingJavaScriptRoot, request.Preview2ShimRoot, request.OutputPath);
        if (request.Imports.IsDefault || request.Arguments.IsDefault || request.Environment.IsDefault
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
            if (!modules.Add(binding.Module) || binding.Functions.IsDefaultOrEmpty
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
        ValidateAbsolutePaths(request.HostingJavaScriptRoot, request.OutputPath, request.DeclarationPath);
        if (Path.GetFileName(request.WorkerFileName) != request.WorkerFileName
            || string.IsNullOrWhiteSpace(request.WorkerFileName)
            || !DigestPattern().IsMatch(request.BuildFingerprint)
            || !DigestPattern().IsMatch(request.ManifestSha256)
            || request.Exports.IsDefaultOrEmpty)
        {
            throw new ArgumentException("Worker client publication contract is invalid.", nameof(request));
        }
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var export in request.Exports)
        {
            ArgumentNullException.ThrowIfNull(export);
            if (string.IsNullOrWhiteSpace(export.Name) || ReservedExportNames.Contains(export.Name)
                || !names.Add(export.Name) || export.Parameters.IsDefault
                || export.AsyncReturn is not null and not "task" and not "value-task")
            {
                throw new ArgumentException("Worker JavaScript exports are invalid.", nameof(request));
            }
            foreach (var parameter in export.Parameters) TypeScriptType(parameter, allowVoid: false);
            TypeScriptType(export.Result, allowVoid: true);
        }
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

    private static string ModuleSpecifier(string path) =>
        Json(new Uri(Path.GetFullPath(path)).AbsoluteUri);

    private static string Json(string value) => JsonSerializer.Serialize(value);

    private static string Json<T>(IEnumerable<T> value) => JsonSerializer.Serialize(value);

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
