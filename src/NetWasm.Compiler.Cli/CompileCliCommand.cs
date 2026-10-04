using System;
using System.Text.Json;

namespace NetWasm.Compiler.Cli;

internal sealed class CompileCliCommand(
    INetWasmCompiler compiler,
    ICompileCliArtifactPlanner artifacts,
    IBinaryFileWriter binaryFiles,
    ITextFileWriter textFiles,
    IFileDeleter files) : ICompilerCliCommand
{
    internal const string CommandName = "";

    private readonly INetWasmCompiler _compiler = compiler ??
        throw new ArgumentNullException(nameof(compiler));
    private readonly ICompileCliArtifactPlanner _artifacts = artifacts ??
        throw new ArgumentNullException(nameof(artifacts));
    private readonly IBinaryFileWriter _binaryFiles = binaryFiles ??
        throw new ArgumentNullException(nameof(binaryFiles));
    private readonly ITextFileWriter _textFiles = textFiles ??
        throw new ArgumentNullException(nameof(textFiles));
    private readonly IFileDeleter _files = files ??
        throw new ArgumentNullException(nameof(files));

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
            EmitStackTrace: options.StackTraceSymbols is not null,
            EntryPointKind: options.EntryPointKind,
            UseJavaScriptExportBoundary: options.UseJavaScriptExportBoundary));
        var artifacts = _artifacts.Plan(options, result);
        if (artifacts.DeleteNativeCallbackObject)
        {
            _files.Delete(artifacts.NativeCallbackObjectPath);
        }
        foreach (var artifact in artifacts.BinarySidecars)
        {
            _binaryFiles.Write(artifact.Path, artifact.Content);
        }
        foreach (var artifact in artifacts.TextSidecars)
        {
            _textFiles.Write(artifact.Path, artifact.Content);
        }
        _binaryFiles.Write(artifacts.OutputPath, artifacts.ApplicationModule);
        return 0;
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
