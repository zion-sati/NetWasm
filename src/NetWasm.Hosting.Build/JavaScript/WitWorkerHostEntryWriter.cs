using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using System.Text.Json;
using NetWasm.Hosting.Execution;
using NetWasm.Hosting.Build;

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
        AppendLine(source, $"import {{ mergeEnvironment }} from {ModuleSpecifier(Path.Combine(request.HostingJavaScriptRoot, "session-options.mjs"))};");
        BrowserHostSource.AppendImports(source, request.Preview2ShimRoot);
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
        source.AppendLine("const configuration = Object.freeze({");
        BrowserHostSource.AppendConfiguration(source, "  ",
            request.ApplicationImports.IsDefault ? [] : request.ApplicationImports,
            request.Arguments, request.Environment, request.Clocks, request.Network, request.Randomness);
        source.AppendLine("});");
        source.AppendLine("const open = request => createBrowserComponentExportSession({");
        source.AppendLine("  configuration: mergeEnvironment(configuration, request.environment),");
        source.AppendLine("  manifestUrl,");
        source.AppendLine("  platform: Object.freeze({");
        BrowserHostSource.AppendPlatformMembers(source, "    ");
        source.AppendLine("  }),");
        source.AppendLine("  stderr: request.stderr,");
        source.AppendLine("  stdout: request.stdout,");
        source.AppendLine("})({ startup: request.startup, notify: request.notify });");
        source.AppendLine("const host = createWorkerSessionHost({");
        source.AppendLine("  openSession: open,");
        source.AppendLine("  post: (message, transfer = []) => globalThis.postMessage(message, transfer),");
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

    private static string Json(string value) => JsonSerializer.Serialize(value, HostingBuildJsonContext.Default.String);


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
