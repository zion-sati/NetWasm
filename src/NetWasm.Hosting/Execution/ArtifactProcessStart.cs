using System.Collections.Immutable;

namespace NetWasm.Hosting.Execution;

internal sealed record ArtifactProcessStart(
    string FileName,
    ImmutableArray<string> Arguments,
    string WorkingDirectory);
