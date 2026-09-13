using System.Collections.Immutable;

namespace NetWasm.Hosting.Execution;

/// <summary>Identifies one built NetWasm artifact and the arguments for one execution.</summary>
public sealed record NetWasmArtifactExecutionRequest(
    string SourcePath,
    ImmutableArray<string> Arguments);
