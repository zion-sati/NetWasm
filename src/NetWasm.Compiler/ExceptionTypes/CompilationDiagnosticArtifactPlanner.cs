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
        var buildId = identities.CalculateBuildId(
            semanticInputs.GetSemanticInputs(options, metadata, plannedTypes));
        var map = mapWriter.Write(plannedTypes, buildId);
        return result with { DiagnosticArtifacts = binder.Bind(result.ApplicationModule, map, buildId) };
    }
}
