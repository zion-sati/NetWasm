using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace NetWasm.Compiler.Diagnostics;

internal static class DiagnosticsServiceCollectionExtensions
{
    public static IServiceCollection AddCompilerDiagnostics(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<CompilerDiagnosticScopeState>();
        services.AddSingleton<ICompilerDiagnosticScopePusher, CompilerDiagnosticScopePusher>();
        services.AddSingleton<ICompilerDiagnosticScopeReader, CompilerDiagnosticScopeReader>();
        services.AddSingleton<ICompilerDiagnosticLogPathResolver,
            CompilerDiagnosticLogPathResolver>();
        services.AddSingleton<ICompilerDiagnosticLineFormatter, CompilerDiagnosticJsonLineFormatter>();
        services.AddSingleton<ICompilerDiagnosticTextWriterFactory, CompilerDiagnosticTextWriterFactory>();
        services.AddSingleton<ICompilerDiagnosticClock, CompilerDiagnosticClock>();
        services.AddSingleton<ICompilerDiagnosticSink, CompilerDiagnosticFileSink>();
        services.AddSingleton<IStructuredBlockOwnershipAnalyzer, StructuredBlockOwnershipAnalyzer>();
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton(typeof(ILogger<>), typeof(CompilerLogger<>));
        services.AddSingleton<ILogger>(static provider =>
            provider.GetRequiredService<ILogger<CompilerDiagnosticCompilationDecorator>>());

        services.AddSingleton<ICompilerDiagnosticTraceWriter,
            CompilerDiagnosticTraceWriter>();
        services.AddSingleton<ICompilerDiagnosticTextArtifactWriter,
            CompilerDiagnosticTextArtifactWriter>();
        services.AddSingleton<ICompilerDiagnosticJsonArtifactWriter,
            CompilerDiagnosticJsonArtifactWriter>();
        services.AddSingleton<ICompilerDiagnosticBinaryArtifactWriter,
            CompilerDiagnosticBinaryArtifactWriter>();
        services.AddSingleton<ICompilerDiagnosticSourceArtifactCopier,
            CompilerDiagnosticSourceArtifactCopier>();
        services.AddSingleton<ICompilerDiagnosticArtifactPathResolver,
            CompilerDiagnosticArtifactPathResolver>();
        services.AddSingleton<ICompilerDiagnosticArtifactEnumerator,
            CompilerDiagnosticArtifactEnumerator>();
        services.AddSingleton<ICompilerDiagnosticFileExistenceReader,
            CompilerDiagnosticFileExistenceReader>();
        services.AddSingleton<ICompilerDiagnosticFileByteReader,
            CompilerDiagnosticFileByteReader>();
        services.AddSingleton<ICompilerDiagnosticTraceFormatter,
            CompilerDiagnosticTraceFormatter>();
        services.AddSingleton<ICompilerDiagnosticLayoutTraceFormatter,
            CompilerDiagnosticLayoutTraceFormatter>();
        services.AddSingleton<ICompilerDiagnosticDispatchTraceFormatter,
            CompilerDiagnosticDispatchTraceFormatter>();
        services.AddSingleton<ICompilerDiagnosticMethodTraceFormatter,
            CompilerDiagnosticMethodTraceFormatter>();
        services.AddSingleton<ICompilerStructuredMethodFormatter,
            CompilerStructuredMethodFormatter>();
        services.AddSingleton<ICompilerCilOperandFormatter,
            CompilerCilOperandFormatter>();
        services.AddSingleton<ICompilerDiagnosticProvenanceProvider,
            CompilerDiagnosticProvenanceProvider>();
        services.AddSingleton<ICompilerSourcePathProvider,
            CompilerSourcePathProvider>();
        services.AddSingleton<ICompilerReproductionCommandBuilder,
            CompilerReproductionCommandBuilder>();
        services.AddSingleton<ICompilerMetadataSnapshotBuilder,
            CompilerMetadataSnapshotBuilder>();
        services.AddSingleton<ICompilerProgramSnapshotBuilder,
            CompilerProgramSnapshotBuilder>();
        services.AddSingleton<ICompilerLayoutSnapshotBuilder,
            CompilerLayoutSnapshotBuilder>();
        services.AddSingleton<ICompilerRootMapSnapshotBuilder,
            CompilerRootMapSnapshotBuilder>();
        services.AddSingleton<ICompilerComplexityMetricsCollector,
            CompilerComplexityMetricsCollector>();
        services.AddSingleton<ICompilerWasmTextWriter,
            CompilerWasmTextWriter>();
        services.AddSingleton<ICompilerInitialDiagnosticArtifactWriter,
            CompilerInitialDiagnosticArtifactWriter>();
        services.AddSingleton<ICompilerMetadataDiagnosticArtifactWriter,
            CompilerMetadataDiagnosticArtifactWriter>();
        services.AddSingleton<ICompilerEntryDiagnosticArtifactWriter,
            CompilerEntryDiagnosticArtifactWriter>();
        services.AddSingleton<ICompilerProgramDiagnosticArtifactWriter,
            CompilerProgramDiagnosticArtifactWriter>();
        services.AddSingleton<ICompilerLayoutDiagnosticArtifactWriter,
            CompilerLayoutDiagnosticArtifactWriter>();
        services.AddSingleton<ICompilerRootMapDiagnosticArtifactWriter,
            CompilerRootMapDiagnosticArtifactWriter>();
        services.AddSingleton<ICompilerComplexityDiagnosticArtifactWriter,
            CompilerComplexityDiagnosticArtifactWriter>();
        services.AddSingleton<ICompilerEmissionDiagnosticArtifactWriter,
            CompilerEmissionDiagnosticArtifactWriter>();
        services.AddSingleton<ICompilerSuccessDiagnosticArtifactWriter,
            CompilerSuccessDiagnosticArtifactWriter>();
        services.AddSingleton<ICompilerFailureDiagnosticArtifactWriter,
            CompilerFailureDiagnosticArtifactWriter>();
        services.AddSingleton<ICompilerProgressReporter,
            CompilerDiagnosticProgressReporter>();
        return services;
    }
}
