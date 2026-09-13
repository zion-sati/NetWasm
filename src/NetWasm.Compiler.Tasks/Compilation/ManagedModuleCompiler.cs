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
        var entryPoint = _entryPoints.Read(request.InputAssemblyPath);
        return _compiler.Compile(new CompilerOptions(
            request.InputAssemblyPath,
            request.ReferencePaths,
            entryPoint.TypeName,
            entryPoint.MethodName,
            ImmutableArray<RequestedExport>.Empty,
            _targets.Resolve(request.Target),
            request.DiagnosticTracePath,
            request.DiagnosticLogPath,
            SourcePaths: request.SourcePaths,
            EmitStackTrace: request.EmitStackTrace,
            EntryMethodToken: entryPoint.MetadataToken,
            EntryPointKind: CompilerEntryPointKind.ManagedExecutable,
            WitPath: request.WitPath,
            WitWorld: request.WitWorld)) with
        {
            EntryPoint = entryPoint.Abi,
        };
    }
}
