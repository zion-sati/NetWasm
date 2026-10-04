using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using System.Text.Json;
using NetWasm.Hosting.Execution;

namespace NetWasm.Hosting.Build.JavaScript;

public sealed record WitWorkerHostEntryRequest(
    string HostingJavaScriptRoot,
    string Preview2ShimRoot,
    string OutputPath,
    ImmutableArray<string> Arguments,
    ImmutableArray<WorkerEnvironmentVariable> Environment,
    ImmutableArray<string> Clocks,
    string Network,
    bool Randomness,
    ImmutableArray<NetWasmApplicationImport> ApplicationImports = default);

public interface IWitWorkerHostEntryWriter
{
    void Write(WitWorkerHostEntryRequest request);
}

public sealed class WitWorkerHostEntryWriter : IWitWorkerHostEntryWriter
{
    public void Write(WitWorkerHostEntryRequest request)
    {
        Validate(request);
        var source = new StringBuilder();
        AppendLine(source, $"import {{ createBrowserComponentExportSession }} from {ModuleSpecifier(Path.Combine(request.HostingJavaScriptRoot, "browser-component-export-session.mjs"))};");
        AppendLine(source, $"import {{ createWorkerSessionHost }} from {ModuleSpecifier(Path.Combine(request.HostingJavaScriptRoot, "worker-session-host.mjs"))};");
        AppendLine(source, $"import {{ WASIShim }} from {ModuleSpecifier(Path.Combine(request.Preview2ShimRoot, "dist", "common", "instantiation.js"))};");
        AppendLine(source, $"import {{ createFilesystem, InMemoryFilesystemAdapter }} from {ModuleSpecifier(Path.Combine(request.Preview2ShimRoot, "dist", "browser", "filesystem.js"))};");
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
        source.AppendLine("const open = createBrowserComponentExportSession({");
        source.AppendLine("  configuration: Object.freeze({");
        source.AppendLine("    applicationImports: Object.freeze([");
        foreach (var binding in request.ApplicationImports.IsDefault ? [] : request.ApplicationImports)
        {
            AppendLine(source, $"      Object.freeze({{ module: {Json(binding.Module)}, artifactPath: {Json(binding.ArtifactPath)}, sha256: {Json(binding.Sha256)} }}),");
        }
        source.AppendLine("    ]),");
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
        source.AppendLine("const host = createWorkerSessionHost({");
        source.AppendLine("  openSession: request => open({ startup: request.startup, notify: request.notify }),");
        source.AppendLine("  post: message => globalThis.postMessage(message),");
        source.AppendLine("  terminate: cause => cause === null ? globalThis.close() : fatal(cause),");
        source.AppendLine("});");
        source.AppendLine("globalThis.addEventListener(\"message\", event => { host.receive(event.data).catch(fatal); });");
        source.AppendLine("globalThis.addEventListener(\"messageerror\", () => fatal(new Error(\"Worker message could not be decoded\")));");
        Write(request.OutputPath, source.ToString());
    }

    private static void Validate(WitWorkerHostEntryRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (new[]
            {
                request.HostingJavaScriptRoot,
                request.Preview2ShimRoot,
                request.OutputPath,
            }.Any(path => string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path))
            || request.Arguments.IsDefault || request.Environment.IsDefault
            || request.Clocks.IsDefault || request.Network is not ("allowAll" or "denyAll")
            || request.Arguments.Any(argument => argument.Contains('\0'))
            || request.Environment.Any(variable => variable is null
                || string.IsNullOrWhiteSpace(variable.Name)
                || variable.Name.Contains('\0') || variable.Value.Contains('\0'))
            || request.Environment.Select(variable => variable.Name)
                .Distinct(StringComparer.Ordinal).Count() != request.Environment.Length
            || request.Clocks.Any(clock => clock is not ("wall" or "monotonic"))
            || request.Clocks.Distinct(StringComparer.Ordinal).Count() != request.Clocks.Length
            || !request.ApplicationImports.IsDefault && (request.ApplicationImports.Any(binding => binding is null
                || string.IsNullOrWhiteSpace(binding.Module) || string.IsNullOrWhiteSpace(binding.ArtifactPath)
                || Path.IsPathRooted(binding.ArtifactPath) || binding.ArtifactPath.Contains('\\')
                || binding.ArtifactPath.Split('/').Any(part => part is "" or "." or "..")
                || binding.Sha256 is not { Length: 64 }
                || binding.Sha256.Any(character => character is not (>= '0' and <= '9') and not (>= 'a' and <= 'f')))
                || request.ApplicationImports.Select(binding => binding.Module).Distinct(StringComparer.Ordinal).Count()
                    != request.ApplicationImports.Length))
        {
            throw new ArgumentException("WIT worker host configuration is invalid.", nameof(request));
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
            source.ReplaceLineEndings("\n"),
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }
}
