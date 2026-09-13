using System.Collections.Immutable;

namespace NetWasm.Hosting.Execution;

/// <summary>The structured infrastructure outcome of one NetWasm execution.</summary>
public sealed record NetWasmExecutionResult(
    int SchemaVersion,
    NetWasmCompletionKind CompletionKind,
    int? ExitCode,
    NetWasmExecutionFailure? PrimaryFailure,
    ImmutableArray<NetWasmExecutionFailure> CleanupFailures);
