using System;
using NetWasm.Compiler;
using NetWasm.Compiler.ExceptionTypes;
using NetWasm.Compiler.Metadata;

namespace NetWasm.Compiler.Pipeline;

internal interface ICompilationDiagnosticBindingStage
{
    CompilationResult Bind(
        CompilationResult result,
        MetadataCompilationSnapshot metadata,
        CompilerOptions options);
}

internal sealed class CompilationDiagnosticBindingStage(
    ICompilationDiagnosticArtifactPlanner planner,
    ITypeIdentityResolverFactory identities) : ICompilationDiagnosticBindingStage
{
    private readonly ICompilationDiagnosticArtifactPlanner _planner = planner ??
        throw new ArgumentNullException(nameof(planner));
    private readonly ITypeIdentityResolverFactory _identities = identities ??
        throw new ArgumentNullException(nameof(identities));

    public CompilationResult Bind(
        CompilationResult result,
        MetadataCompilationSnapshot metadata,
        CompilerOptions options)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentNullException.ThrowIfNull(options);
        return _planner.Bind(result, metadata, _identities.Create(metadata), options);
    }
}
