using System;
using System.Collections.Immutable;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Cli;

internal sealed record ComponentizeCliOptions(
    string CoreModule,
    string? RuntimeModule,
    string Wit,
    string? World,
    string Output,
    string Manifest,
    WasmTarget Target,
    string? InteropManifest,
    string? JcoVersion,
    string? Preview2ShimVersion)
{
    public static ComponentizeCliOptions Parse(string[] arguments)
    {
        string? coreModule = null;
        string? runtimeModule = null;
        string? wit = null;
        string? world = null;
        string? output = null;
        string? manifest = null;
        string? target = null;
        string? interopManifest = null;
        string? jcoVersion = null;
        string? preview2ShimVersion = null;
        CliOptionReader.Read(arguments, (option, value) =>
        {
            switch (option)
            {
                case "--core-module": coreModule = SetOnce(coreModule, value, option); break;
                case "--runtime-module": runtimeModule = SetOnce(runtimeModule, value, option); break;
                case "--wit": wit = SetOnce(wit, value, option); break;
                case "--world": world = SetOnce(world, value, option); break;
                case "--output": output = SetOnce(output, value, option); break;
                case "--manifest": manifest = SetOnce(manifest, value, option); break;
                case "--target": target = SetOnce(target, value, option); break;
                case "--interop-manifest": interopManifest = SetOnce(interopManifest, value, option); break;
                case "--jco-version": jcoVersion = SetOnce(jcoVersion, value, option); break;
                case "--preview2-shim-version": preview2ShimVersion = SetOnce(preview2ShimVersion, value, option); break;
                default:
                    throw CliOptionException.Create(
                    $"unknown componentize option '{option}'");
            }
        });
        return new ComponentizeCliOptions(
            coreModule ?? throw CliOptionException.Create("missing required option '--core-module'"),
            runtimeModule,
            wit ?? throw CliOptionException.Create("missing required option '--wit'"),
            world,
            output ?? throw CliOptionException.Create("missing required option '--output'"),
            manifest ?? throw CliOptionException.Create("missing required option '--manifest'"),
            ParseTarget(target),
            interopManifest,
            jcoVersion,
            preview2ShimVersion);
    }

    private static WasmTarget ParseTarget(string? value) => value switch
    {
        null or "wasm32" => WasmTarget.Wasm32,
        "wasm64" => WasmTarget.Wasm64,
        _ => throw CliOptionException.Create("target must be 'wasm32' or 'wasm64'"),
    };

    private static string SetOnce(string? current, string value, string option) =>
        current is null
            ? value
            : throw CliOptionException.Create(
                $"option '{option}' was specified more than once");
}

internal sealed record CompileCliOptions(
    string Input,
    string Output,
    string EntryType,
    string EntryMethod,
    ImmutableArray<string> References,
    ImmutableArray<RequestedExport> Exports,
    WasmTarget Target,
    string? InteropManifest,
    string? RuntimeLayout,
    string? DiagnosticTrace,
    string? DiagnosticLog,
    string? StackTraceSymbols,
    string? Wit,
    string? World,
    ImmutableArray<string> Sources)
{
    public static CompileCliOptions Parse(string[] arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        string? input = null;
        string? output = null;
        string? entry = null;
        string? target = null;
        string? interopManifest = null;
        string? runtimeLayout = null;
        string? diagnosticTrace = null;
        string? diagnosticLog = null;
        string? stackTraceSymbols = null;
        string? wit = null;
        string? world = null;
        var references = ImmutableArray.CreateBuilder<string>();
        var exports = ImmutableArray.CreateBuilder<RequestedExport>();
        var sources = ImmutableArray.CreateBuilder<string>();
        CliOptionReader.Read(arguments, (option, value) =>
        {
            switch (option)
            {
                case "--input": input = SetOnce(input, value, option); break;
                case "--output": output = SetOnce(output, value, option); break;
                case "--entry": entry = SetOnce(entry, value, option); break;
                case "--target": target = SetOnce(target, value, option); break;
                case "--interop-manifest": interopManifest = SetOnce(interopManifest, value, option); break;
                case "--runtime-layout": runtimeLayout = SetOnce(runtimeLayout, value, option); break;
                case "--diagnostic-trace": diagnosticTrace = SetOnce(diagnosticTrace, value, option); break;
                case "--diagnostic-log": diagnosticLog = SetOnce(diagnosticLog, value, option); break;
                case "--stack-trace-symbols": stackTraceSymbols = SetOnce(stackTraceSymbols, value, option); break;
                case "--wit": wit = SetOnce(wit, value, option); break;
                case "--world": world = SetOnce(world, value, option); break;
                case "--reference": references.Add(value); break;
                case "--export": exports.Add(ParseExport(value)); break;
                case "--source": sources.Add(value); break;
                default: throw CliOptionException.Create($"unknown option '{option}'");
            }
        });

        (var entryType, var entryMethod) = ParseMethod(
            entry ?? throw CliOptionException.Create("missing required option '--entry'"));
        return new CompileCliOptions(
            input ?? throw CliOptionException.Create("missing required option '--input'"),
            output ?? throw CliOptionException.Create("missing required option '--output'"),
            entryType,
            entryMethod,
            references.ToImmutable(),
            exports.ToImmutable(),
            target switch
            {
                null or "wasm32" => WasmTarget.Wasm32,
                "wasm64" => WasmTarget.Wasm64,
                _ => throw CliOptionException.Create("target must be 'wasm32' or 'wasm64'"),
            },
            interopManifest,
            runtimeLayout,
            diagnosticTrace,
            diagnosticLog,
            stackTraceSymbols,
            wit,
            world,
            sources.ToImmutable());
    }

    private static RequestedExport ParseExport(string value)
    {
        var separator = value.IndexOf('=', StringComparison.Ordinal);
        if (separator <= 0 || separator == value.Length - 1)
        {
            throw CliOptionException.Create(
                "export must use 'name=Namespace.Type::Method' syntax");
        }
        var name = value[..separator];
        var (type, method) = ParseMethod(value[(separator + 1)..]);
        return new RequestedExport(name, type, method);
    }

    private static (string Type, string Method) ParseMethod(string value)
    {
        var parts = value.Split("::", 2, StringSplitOptions.None);
        if (parts.Length != 2 || parts[0].Length == 0 || parts[1].Length == 0)
        {
            throw CliOptionException.Create(
                "method must use 'Namespace.Type::Method' syntax");
        }
        return (parts[0], parts[1]);
    }

    private static string SetOnce(string? current, string value, string option) =>
        current is null
            ? value
            : throw CliOptionException.Create(
                $"option '{option}' was specified more than once");
}

internal static class CliOptionReader
{
    public static void Read(string[] arguments, Action<string, string> read)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentNullException.ThrowIfNull(read);
        for (var index = 0; index < arguments.Length; index++)
        {
            var option = arguments[index];
            var value = index + 1 < arguments.Length
                ? arguments[++index]
                : throw CliOptionException.Create($"option '{option}' requires a value");
            read(option, value);
        }
    }

}
