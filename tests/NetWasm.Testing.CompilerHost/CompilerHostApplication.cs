using System.Collections.Immutable;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using NetWasm.Compiler;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Diagnostics;
using NetWasm.Compiler.Metadata;

namespace NetWasm.Testing.CompilerHost;

internal interface ICompilerHostRequestRunner
{
    int Run(CompilationRequest request, string responsePath);
}

internal sealed class CompilerHostApplication(
    INetWasmCompiler compiler,
    IMetadataCompilationLoader metadataLoader) : ICompilerHostRequestRunner
{
    private readonly INetWasmCompiler _compiler =
        compiler ?? throw new ArgumentNullException(nameof(compiler));
    private readonly IMetadataCompilationLoader _metadataLoader =
        metadataLoader ?? throw new ArgumentNullException(nameof(metadataLoader));

    public int Run(CompilationRequest request, string responsePath)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(responsePath);
        var adapterStarted = Stopwatch.GetTimestamp();
        var metrics = new CompilerHostMetricsObserver();
        try
        {
            var result = _compiler.Compile(new CompilerOptions(
                request.EntryAssemblyPath,
                request.ReferencePaths,
                request.EntryTypeName,
                request.EntryMethodName,
                request.Exports,
                request.Target,
                request.DiagnosticTracePath,
                WitPath: request.WitPath,
                WitWorld: request.WitWorld,
                SourcePaths: request.SourcePaths,
                ReferenceAssemblyAliases: request.ReferenceAssemblyAliases,
                EmitStackTrace: request.EmitStackTrace,
                EntryPointKind: request.EntryPointKind,
                MetricsObserver: request.CollectCompilerMetrics ? metrics : null,
                EnableFrontendCache: request.EnableFrontendCache,
                IntermediateOutputPath: request.IntermediateOutputPath));
            File.WriteAllBytes(request.ModulePath, result.ApplicationModule);
            if (request.EmitStackTrace)
            {
                ArgumentException.ThrowIfNullOrWhiteSpace(
                    request.StackTraceSymbolsPath);
                File.WriteAllBytes(
                    request.StackTraceSymbolsPath,
                    result.StackTraceSymbols?.Bytes ?? throw new InvalidOperationException(
                        "instrumented compilation produced no stack-trace sidecar"));
            }

            using var metadata = request.ReferenceAssemblyAliases.Count == 0
                ? _metadataLoader.Load(request.EntryAssemblyPath, request.ReferencePaths)
                : _metadataLoader.Load(
                    request.EntryAssemblyPath,
                    request.ReferencePaths,
                    request.ReferenceAssemblyAliases);
            var typeNames = result.Layouts.TypeDescriptors.ToImmutableDictionary(
                descriptor => descriptor.TypeId,
                descriptor => metadata.Snapshot.Types
                    .Single(type => type.Key == descriptor.Type)
                    .FullName);
            var adapterDuration = Stopwatch.GetElapsedTime(adapterStarted);
            File.WriteAllText(responsePath, JsonSerializer.Serialize(new CompilationResponse(
                typeNames,
                Convert.ToHexString(SHA256.HashData(result.ApplicationModule))
                    .ToLowerInvariant(),
                result.StaticDataEnd,
                adapterDuration,
                CreateAdapterTiming(adapterDuration, metrics.Report),
                metrics.Report)));
            return 0;
        }
        catch (CompilerException exception) when (request.CaptureDiagnostic)
        {
            var adapterDuration = Stopwatch.GetElapsedTime(adapterStarted);
            File.WriteAllText(responsePath, JsonSerializer.Serialize(
                new CapturedDiagnosticResponse(
                    (int)exception.Diagnostic.Code,
                    exception.Diagnostic.Message,
                    exception.Diagnostic.Method,
                    exception.Diagnostic.IlOffset,
                    adapterDuration,
                    CreateAdapterTiming(adapterDuration, metrics.Report),
                    metrics.Report)));
            return 0;
        }
        catch (Exception exception)
        {
            var adapterDuration = Stopwatch.GetElapsedTime(adapterStarted);
            File.WriteAllText(responsePath, JsonSerializer.Serialize(
                new FailedCompilationResponse(
                    exception is OperationCanceledException
                        ? CompilerMetricsOutcome.Canceled
                        : CompilerMetricsOutcome.Failed,
                    adapterDuration,
                    CreateAdapterTiming(adapterDuration, metrics.Report),
                    metrics.Report)));
            Console.Error.WriteLine(exception);
            return 1;
        }
    }

    private static CompilerAdapterTiming? CreateAdapterTiming(
        TimeSpan adapterDuration,
        CompilerMetricsReport? compilerMetrics) =>
        compilerMetrics is null
            ? null
            : new(
                adapterDuration,
                compilerMetrics.TotalDuration,
                adapterDuration - compilerMetrics.TotalDuration);
}
