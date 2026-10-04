using System.Collections.Immutable;

namespace NetWasm.Compiler.Tasks.Compilation;

internal sealed class ManagedModuleCompiler(
    IManagedEntryPointReader entryPoints,
    IWasmTargetResolver targets,
    INetWasmCompilationInvoker compiler) : IManagedModuleCompiler
{
    private readonly IManagedEntryPointReader _entryPoints = entryPoints ??
        throw new ArgumentNullException(nameof(entryPoints));
    private readonly IWasmTargetResolver _targets = targets ??
        throw new ArgumentNullException(nameof(targets));
    private readonly INetWasmCompilationInvoker _compiler = compiler ??
        throw new ArgumentNullException(nameof(compiler));

    public ManagedModuleCompilation Compile(ManagedModuleCompileRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var entryPoint = request.CompileAsLibrary
            ? null
            : _entryPoints.Read(request.InputAssemblyPath);
        return _compiler.Compile(new CompilerOptions(
            request.InputAssemblyPath,
            request.ReferencePaths,
            entryPoint?.TypeName ?? string.Empty,
            entryPoint?.MethodName ?? string.Empty,
            ImmutableArray<RequestedExport>.Empty,
            _targets.Resolve(request.Target),
            request.DiagnosticTracePath,
            request.DiagnosticLogPath,
            SourcePaths: request.SourcePaths,
            EmitStackTrace: request.EmitStackTrace,
            EntryMethodToken: entryPoint?.MetadataToken,
            EntryPointKind: request.CompileAsLibrary
                ? CompilerEntryPointKind.Library
                : CompilerEntryPointKind.ManagedExecutable,
            WitPath: request.WitPath,
            WitWorld: request.WitWorld,
            EnableFrontendCache: true,
            IntermediateOutputPath: request.IntermediateOutputPath,
            UseJavaScriptExportBoundary: request.UseJavaScriptExportBoundary,
            ProjectDirectory: request.ProjectDirectory,
            PathMap: request.PathMap,
            StructuredDiagnostics: request.StructuredDiagnostics)) with
        {
            EntryPoint = entryPoint?.Abi,
        };
    }
}
