using System;
using NetWasm.Compiler;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Metadata;

namespace NetWasm.Compiler.ExceptionTypes;

public interface ICompilationDiagnosticArtifactPlanner
{
    CompilationResult Bind(
        CompilationResult result,
        MetadataCompilationSnapshot metadata,
        ITypeIdentityResolver typeIdentities,
        CompilerOptions options);

    // A module's diagnostic artifacts from exception types planned before: a compile that
    // keeps the last one's layouts keeps their exception types.
    DiagnosticArtifactBundle? Bind(
        byte[] applicationModule,
        System.Collections.Immutable.ImmutableArray<ReachableExceptionType> planned,
        MetadataCompilationSnapshot metadata,
        CompilerOptions options);
}

public sealed class CompilationDiagnosticArtifactPlanner(
    IReachableExceptionTypePlanner exceptionTypes,
    IAssemblyIdentityFormatterFactory assemblyFormatters,
    IExceptionTypeMapWriter mapWriter,
    ICompilationSemanticInputProvider semanticInputs,
    IDiagnosticArtifactIdentityCalculator identities,
    IDiagnosticArtifactBinder binder) : ICompilationDiagnosticArtifactPlanner
{
    public CompilationResult Bind(
        CompilationResult result,
        MetadataCompilationSnapshot metadata,
        ITypeIdentityResolver typeIdentities,
        CompilerOptions options)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentNullException.ThrowIfNull(typeIdentities);
        ArgumentNullException.ThrowIfNull(options);
        if (!options.EmitExceptionTypeMap)
            return result with { DiagnosticArtifacts = null };
        var plannedTypes = exceptionTypes.Plan(
            metadata,
            typeIdentities,
            assemblyFormatters.Create(metadata.Assemblies),
            result.Layouts);
        return result with
        {
            DiagnosticArtifacts = Bind(result.ApplicationModule, [.. plannedTypes], metadata, options),
        };
    }

    // The map of the planned types bound to the module; its callers ask for it only where the
    // options want one.
    public DiagnosticArtifactBundle? Bind(
        byte[] applicationModule,
        System.Collections.Immutable.ImmutableArray<ReachableExceptionType> planned,
        MetadataCompilationSnapshot metadata,
        CompilerOptions options)
    {
        ArgumentNullException.ThrowIfNull(applicationModule);
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentNullException.ThrowIfNull(options);
        var buildId = identities.CalculateBuildId(
            semanticInputs.GetSemanticInputs(options, metadata, planned));
        var map = mapWriter.Write(planned, buildId);
        return binder.Bind(applicationModule, map, buildId) with { ExceptionTypes = planned };
    }
}
