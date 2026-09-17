using System.Collections.Immutable;
using NetWasm.Compiler;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Diagnostics;

namespace NetWasm.Testing.CompilerHost;

internal sealed record CompilationRequest(
    string EntryAssemblyPath,
    ImmutableArray<string> ReferencePaths,
    string EntryTypeName,
    string EntryMethodName,
    ImmutableArray<RequestedExport> Exports,
    WasmTarget Target,
    string? DiagnosticTracePath,
    ImmutableArray<string> SourcePaths,
    ImmutableDictionary<string, string> ReferenceAssemblyAliases,
    string ModulePath,
    bool CaptureDiagnostic = false,
    string? WitPath = null,
    string? WitWorld = null,
    bool EmitStackTrace = false,
    string? StackTraceSymbolsPath = null,
    CompilerEntryPointKind EntryPointKind = CompilerEntryPointKind.RawFunction,
    bool CollectCompilerMetrics = true,
    bool EnableFrontendCache = false,
    string? IntermediateOutputPath = null);

internal sealed record CompilationResponse(
    ImmutableDictionary<int, string> TypeNames,
    string ModuleSha256,
    int StaticDataEnd,
    TimeSpan AdapterDuration,
    CompilerAdapterTiming? CompilerTiming,
    CompilerMetricsReport? CompilerMetrics);

internal sealed record CapturedDiagnosticResponse(
    int Code,
    string Message,
    string? Method,
    int? IlOffset,
    TimeSpan AdapterDuration,
    CompilerAdapterTiming? CompilerTiming,
    CompilerMetricsReport? CompilerMetrics);

internal sealed record FailedCompilationResponse(
    CompilerMetricsOutcome Outcome,
    TimeSpan AdapterDuration,
    CompilerAdapterTiming? CompilerTiming,
    CompilerMetricsReport? CompilerMetrics);

internal sealed record CompilationSequenceRequest(
    ImmutableArray<CompilationSequenceStep> Steps);

internal sealed record CompilationSequenceStep(
    string Label,
    CompilationRequest Request,
    string ResponsePath);

internal sealed record CompilationSequenceReceipt(
    int SchemaVersion,
    ImmutableArray<CompilationSequenceStepReceipt> Steps);

internal sealed record CompilationSequenceStepReceipt(
    string Label,
    int ExitCode,
    CompilerMetricsOutcome Outcome,
    double ElapsedMilliseconds,
    TimeSpan AdapterDuration,
    CompilerAdapterTiming? CompilerTiming,
    string? ModuleSha256,
    CompilerMetricsReport? CompilerMetrics);
