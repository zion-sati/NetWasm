using System;
using Microsoft.Extensions.DependencyInjection;

namespace NetWasm.Compiler.ExceptionTypes;

internal static class ExceptionTypesServiceCollectionExtensions
{
    public static IServiceCollection AddCompilerExceptionTypes(
        this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddSingleton<IReachableExceptionTypePlanner,
            ReachableExceptionTypePlanner>();
        services.AddSingleton<IReachableExceptionTypeValidator,
            ReachableExceptionTypeValidator>();
        services.AddSingleton<IArtifactDigestCalculator,
            ArtifactDigestCalculator>();
        services.AddSingleton<IExceptionTypeMapIntegrityVerifier,
            ExceptionTypeMapIntegrityVerifier>();
        services.AddSingleton<IExceptionTypeMapWriter,
            ExceptionTypeMapWriter>();
        services.AddSingleton<ICompilationInputHasher,
            CompilationInputHasher>();
        services.AddSingleton<ICompilationSemanticInputProvider,
            CompilationSemanticInputProvider>();
        services.AddSingleton<IDiagnosticArtifactIdentityCalculator,
            DiagnosticArtifactIdentityCalculator>();
        services.AddSingleton<IDiagnosticArtifactManifestWriter,
            DiagnosticArtifactManifestWriter>();
        services.AddSingleton<IDiagnosticArtifactBinder,
            DiagnosticArtifactBinder>();
        services.AddSingleton<ICompilationDiagnosticArtifactPlanner,
            CompilationDiagnosticArtifactPlanner>();
        return services;
    }
}
