// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
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
        // Every entry goes to each provider, which decides what it keeps: the
        // diagnostic log keeps everything inside an enabled compilation scope.
        services.AddLogging(logging => logging.SetMinimumLevel(LogLevel.Trace));
        services.AddSingleton<ILoggerProvider, CompilerDiagnosticFileLoggerProvider>();
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
        services.AddSingleton<ICompilerReceiverFlowReportWriter, CompilerReceiverFlowReportWriter>();
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
        services.AddSingleton<ICompilerMemoryReader>(static _ =>
            new ProcessCompilerMemoryReader(hasProcess: !(OperatingSystem.IsBrowser() | OperatingSystem.IsWasi())));
        // Within a stage: every 2 GiB of growth. Between stages: whenever 2 GiB are committed.
        services.AddSingleton<ICompilerMemoryReleaser>(static provider => new CollectingCompilerMemoryReleaser(
            hasProcess: !(OperatingSystem.IsBrowser() | OperatingSystem.IsWasi()),
            provider.GetRequiredService<ICompilerMemoryReader>(),
            floor: 0,
            growth: CollectingCompilerMemoryReleaser.ClosureGrowth,
            provider.GetRequiredService<ILogger<CollectingCompilerMemoryReleaser>>()));
        services.TryAddSingleton<ICompilerMemoryWatch, UnwatchedCompilerMemory>();
        services.AddSingleton<ICompilerProgressReporter>(static provider => new MemoryReleasingProgressReporter(
            new MemoryReportingProgressReporter(
                ActivatorUtilities.CreateInstance<CompilerDiagnosticProgressReporter>(provider),
                provider.GetRequiredService<ICompilerMemoryReader>(),
                provider.GetRequiredService<ILogger<MemoryReportingProgressReporter>>(),
                provider.GetRequiredService<TimeProvider>()),
            new CollectingCompilerMemoryReleaser(
                hasProcess: !(OperatingSystem.IsBrowser() | OperatingSystem.IsWasi()),
                provider.GetRequiredService<ICompilerMemoryReader>(),
                floor: CollectingCompilerMemoryReleaser.StageFloor,
                growth: 0,
                provider.GetRequiredService<ILogger<CollectingCompilerMemoryReleaser>>())));
        return services;
    }
}
