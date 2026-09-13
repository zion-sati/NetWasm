using System;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Diagnostics;
using NetWasm.Compiler.Metadata;
using NetWasm.Compiler.Validation;

namespace NetWasm.Compiler.Pipeline;

internal interface ICompilationMetadataStage
{
    void ValidateAndWrite(CompilerOptions options, MetadataCompilationSnapshot metadata);
}

internal sealed class CompilationMetadataStage(
    IMetadataInvariantValidator invariants,
    ICompilerMetadataDiagnosticArtifactWriter artifacts,
    ITypeRepositoryFactory types,
    IMethodBodyReaderFactory bodies,
    ISymbolFormatterFactory symbols) : ICompilationMetadataStage
{
    private readonly IMetadataInvariantValidator _invariants = invariants ??
        throw new ArgumentNullException(nameof(invariants));
    private readonly ICompilerMetadataDiagnosticArtifactWriter _artifacts = artifacts ??
        throw new ArgumentNullException(nameof(artifacts));
    private readonly ITypeRepositoryFactory _types = types ??
        throw new ArgumentNullException(nameof(types));
    private readonly IMethodBodyReaderFactory _bodies = bodies ??
        throw new ArgumentNullException(nameof(bodies));
    private readonly ISymbolFormatterFactory _symbols = symbols ??
        throw new ArgumentNullException(nameof(symbols));

    public void ValidateAndWrite(
        CompilerOptions options,
        MetadataCompilationSnapshot metadata)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(metadata);
        var types = _types.Create(metadata);
        var symbols = _symbols.Create(metadata);
        _artifacts.WriteMetadata(options, metadata, _bodies.Create(metadata), symbols);
        _invariants.Validate(metadata, types, symbols);
    }
}
