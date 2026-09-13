using System;
using System.Text.Json;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Cli;

internal sealed class CompileCliCommand(
    INetWasmCompiler compiler,
    IBinaryFileWriter binaryFiles,
    ITextFileWriter textFiles) : ICompilerCliCommand
{
    internal const string CommandName = "";

    private readonly INetWasmCompiler _compiler = compiler ??
        throw new ArgumentNullException(nameof(compiler));
    private readonly IBinaryFileWriter _binaryFiles = binaryFiles ??
        throw new ArgumentNullException(nameof(binaryFiles));
    private readonly ITextFileWriter _textFiles = textFiles ??
        throw new ArgumentNullException(nameof(textFiles));

    public string Name => CommandName;

    public int Run(string[] arguments)
    {
        var options = CompileCliOptions.Parse(arguments);
        var result = _compiler.Compile(new CompilerOptions(
            options.Input,
            options.References,
            options.EntryType,
            options.EntryMethod,
            options.Exports,
            options.Target,
            options.DiagnosticTrace,
            options.DiagnosticLog,
            options.Wit,
            options.World,
            options.Sources,
            EmitStackTrace: options.StackTraceSymbols is not null));
        _binaryFiles.Write(options.Output, result.ApplicationModule);
        WriteRuntimeLayout(options, result, _textFiles);
        WriteStackTraceSymbols(options, result, _binaryFiles);
        WriteInteropManifest(options, result, _textFiles);
        return 0;
    }

    private static void WriteStackTraceSymbols(
        CompileCliOptions options,
        CompilationResult result,
        IBinaryFileWriter files)
    {
        if (options.StackTraceSymbols is null)
        {
            return;
        }
        var artifact = result.StackTraceSymbols ??
            throw new InvalidOperationException(
                "stack-trace instrumentation did not produce a symbol sidecar");
        files.Write(options.StackTraceSymbols, artifact.Bytes);
    }

    private static void WriteRuntimeLayout(
        CompileCliOptions options,
        CompilationResult result,
        ITextFileWriter textFiles)
    {
        if (options.RuntimeLayout is null)
        {
            return;
        }
        textFiles.Write(
            options.RuntimeLayout,
            CliJson.Serialize(new
            {
                schemaVersion = 2,
                target = options.Target == WasmTarget.Wasm64 ? "wasm64" : "wasm32",
                applicationStaticDataEnd = result.StaticDataEnd,
            }));
    }

    private static void WriteInteropManifest(
        CompileCliOptions options,
        CompilationResult result,
        ITextFileWriter textFiles)
    {
        if (options.InteropManifest is not null)
        {
            textFiles.Write(
                options.InteropManifest,
                CliJson.Serialize(result.InteropManifest));
        }
    }

}

internal static class CliJson
{
    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        NewLine = "\n",
    };

    public static string Serialize<T>(T value) =>
        JsonSerializer.Serialize(value, WriteOptions);
}
