using System.Collections.Immutable;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using NetWasm.Hosting.Build;

namespace NetWasm.Hosting.Build.JavaScript;

public sealed record CommandRunModuleRequest(
    string BundleFileName,
    string BuildFingerprint,
    string ManifestSha256,
    string OutputPath,
    string DeclarationPath,
    ImmutableArray<string> Arguments,
    ImmutableArray<WorkerEnvironmentVariable> Environment,
    ImmutableArray<string> Clocks,
    string Network,
    bool Randomness);

public interface ICommandRunModuleWriter
{
    /// <summary>Writes the typed <c>run()</c> module that pins one published command deployment.</summary>
    void Write(CommandRunModuleRequest request);
}

public sealed partial class CommandRunModuleWriter : ICommandRunModuleWriter
{
    public void Write(CommandRunModuleRequest request)
    {
        Validate(request);
        var source = new StringBuilder();
        source.Append("import { runNetWasm } from ").Append(Json("./" + request.BundleFileName)).AppendLine(";");
        source.AppendLine();
        source.AppendLine("const startup = Object.freeze({");
        source.Append("  buildFingerprint: ").Append(Json(request.BuildFingerprint)).AppendLine(",");
        source.Append("  manifestSha256: ").Append(Json(request.ManifestSha256)).AppendLine(",");
        source.AppendLine("});");
        source.AppendLine("const configuration = Object.freeze({");
        BrowserHostSource.AppendConfiguration(source, "  ", [], request.Arguments, request.Environment,
            request.Clocks, request.Network, request.Randomness);
        source.AppendLine("});");
        source.AppendLine("export function run(options) {");
        source.AppendLine("  return runNetWasm(Object.freeze({ configuration, options, startup }));");
        source.AppendLine("}");
        Write(request.OutputPath, source.ToString());
        Write(request.DeclarationPath, Declaration);
    }

    private const string Declaration = """
        export interface NetWasmRunOptions {
          arguments?: readonly string[];
          environment?: Record<string, string>;
          signal?: AbortSignal | null;
          stderr?: (line: string) => void;
          stdout?: (line: string) => void;
        }
        export interface NetWasmExecutionFailure {
          readonly phase: "validation" | "instantiation" | "execution" | "observation" | "cleanup" | "output";
          readonly code: string;
          readonly message: string;
          readonly detail?: string;
        }
        export interface NetWasmExecutionResult {
          readonly schemaVersion: 1;
          readonly completionKind: "normal" | "managedFailure" | "managedCancellation" | "callerCancellation" | "contractFailure" | "hostFailure";
          readonly exitCode: number | null;
          readonly primaryFailure: NetWasmExecutionFailure | null;
          readonly cleanupFailures: readonly NetWasmExecutionFailure[];
        }
        export declare function run(options?: NetWasmRunOptions): Promise<NetWasmExecutionResult>;

        """;

    private static void Validate(CommandRunModuleRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (new[] { request.OutputPath, request.DeclarationPath }
                .Any(path => string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path))
            || string.IsNullOrWhiteSpace(request.BundleFileName)
            || Path.GetFileName(request.BundleFileName) != request.BundleFileName
            || !DigestPattern().IsMatch(request.BuildFingerprint)
            || !DigestPattern().IsMatch(request.ManifestSha256)
            || request.Arguments.IsDefaultOrEmpty || request.Environment.IsDefault || request.Clocks.IsDefault
            || request.Network is not ("allowAll" or "denyAll")
            || request.Arguments.Any(argument => argument.Contains('\0'))
            || request.Environment.Any(variable => string.IsNullOrWhiteSpace(variable.Name)
                || variable.Name.Contains('\0') || variable.Value.Contains('\0'))
            || request.Environment.Select(variable => variable.Name).Distinct(StringComparer.Ordinal).Count()
                != request.Environment.Length
            || request.Clocks.Any(clock => clock is not ("wall" or "monotonic"))
            || request.Clocks.Distinct(StringComparer.Ordinal).Count() != request.Clocks.Length)
        {
            throw new ArgumentException("Command run module publication contract is invalid.", nameof(request));
        }
    }

    private static string Json(string value) => JsonSerializer.Serialize(value, HostingBuildJsonContext.Default.String);

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
