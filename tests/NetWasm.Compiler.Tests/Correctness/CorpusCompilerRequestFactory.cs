using System.Collections.Immutable;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Tests.Correctness;

internal sealed record CorpusCompilerRequest(
    string EntryAssemblyPath,
    ImmutableArray<string> ReferencePaths,
    string EntryTypeName,
    string EntryMethodName,
    ImmutableArray<RequestedExport> Exports,
    WasmTarget Target,
    string? DiagnosticTracePath,
    ImmutableArray<string> SourcePaths,
    ImmutableDictionary<string, string> ReferenceAssemblyAliases,
    string? WitPath,
    string? WitWorld,
    string ModulePath,
    bool EmitStackTrace,
    string? StackTraceSymbolsPath,
    string? RuntimeLayoutPath,
    string? InteropManifestPath);

internal interface ICorpusCompilerRequestFactory
{
    CorpusCompilerRequest Create(
        CorpusCompilation compilation, WasmTarget target, string? tracePath,
        string modulePath, string? stackTraceSymbolsPath,
        ImmutableArray<RequestedExport> exports,
        string? runtimeLayoutPath = null, string? interopManifestPath = null);
}

internal sealed class CorpusCompilerRequestFactory(
    CompilerCorrectnessEnvironment environment,
    IOracleModePolicyRegistry oracleModes) : ICorpusCompilerRequestFactory
{
    public CorpusCompilerRequest Create(
        CorpusCompilation compilation, WasmTarget target, string? tracePath,
        string modulePath, string? stackTraceSymbolsPath,
        ImmutableArray<RequestedExport> exports,
        string? runtimeLayoutPath = null, string? interopManifestPath = null)
    {
        ArgumentNullException.ThrowIfNull(compilation);
        var aliases = oracleModes.Get(compilation.Fixture.OracleMode).ReferenceAssemblyAliases;
        foreach (var (sourceName, targetName) in compilation.Fixture.ReferenceAssemblyAliases)
        {
            if (aliases.TryGetValue(sourceName, out var existingTarget) &&
                !StringComparer.Ordinal.Equals(existingTarget, targetName))
            {
                throw new InvalidOperationException(
                    $"fixture alias '{sourceName}' conflicts with oracle alias '{existingTarget}'");
            }

            aliases = aliases.SetItem(sourceName, targetName);
        }

        ImmutableArray<string> sources = compilation.Sources.IsEmpty
            ? compilation.Profile == CilProfile.Emitted
                ? []
                : [Path.Combine(compilation.Directory, compilation.Fixture.Name + ".cs")]
            : [.. compilation.Sources.Select(source => source.Path)];
        return new(
            compilation.NetWasm.AssemblyPath,
            [environment.CoreLibPath, .. compilation.Fixture.NetWasmReferencePaths],
            compilation.Fixture.EntryType,
            compilation.Fixture.WasmEntryMethod,
            exports, target, tracePath, sources, aliases,
            compilation.Fixture.RequiresReactor
                ? Path.Combine(environment.RepositoryRoot, "wit", "netwasm-platform-1.0.0")
                : null,
            compilation.Fixture.RequiresReactor
                ? "netwasm:platform@1.0.0/async-platform"
                : null,
            modulePath, compilation.Fixture.EmitStackTrace, stackTraceSymbolsPath,
            runtimeLayoutPath, interopManifestPath);
    }
}
