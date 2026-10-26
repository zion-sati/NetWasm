using System;
using System.Text.Json;
using NetWasm.Compiler.ComponentModel.Optimization;

namespace NetWasm.Compiler.Cli;

internal sealed class CompileCliCommand(
    INetWasmCompiler compiler,
    ICompileCliArtifactPlanner artifacts,
    IBinaryFileWriter binaryFiles,
    ITextFileWriter textFiles,
    IFileDeleter files,
    IManagedCompilerOptimizationOverridesParser optimizationOverrides) :
    ICompilerCliCommand
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
    private readonly IManagedCompilerOptimizationOverridesParser _optimizationOverrides =
        optimizationOverrides ??
        throw new ArgumentNullException(nameof(optimizationOverrides));

    public string Name => CommandName;

    public int Run(string[] arguments)
    {
        var options = CompileCliOptions.Parse(arguments);
        var overrides = _optimizationOverrides.Parse(
            options.ManagedLoopInvariantFieldHoisting,
            options.ManagedStaticDataViewElimination,
            options.ManagedAddressTakenValuePromotion,
            options.ManagedMethodInlining,
            options.ManagedBranchPrefixDuplication,
            options.ManagedScalarCallPreservation,
            options.ManagedArrayBoundsCheckElimination,
            options.ManagedLoopUnrolling,
            options.ManagedConstantArgumentPropagation,
            options.ManagedStaticArrayPopulationElimination,
            options.ManagedConstantCallFolding,
            options.ManagedSwitchBitTests,
            options.ManagedDevirtualization,
            options.ManagedAllocationElimination,
            options.ManagedLoopRotation,
            options.ManagedArrayStoreCheckElimination);
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
            UseJavaScriptExportBoundary: options.UseJavaScriptExportBoundary,
            Optimization: options.Optimization,
            OptimizationOverrides: overrides)
        {
            ImportModules = options.ImportModules,
        });
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
