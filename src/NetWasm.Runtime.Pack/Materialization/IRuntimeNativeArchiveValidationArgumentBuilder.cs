using System.Collections.Immutable;

namespace NetWasm.Runtime.Pack.Materialization;

internal interface IRuntimeNativeArchiveValidationArgumentBuilder
{
    ImmutableArray<string> Build(RuntimeNativeArchiveValidationRequest request);
}

internal sealed record RuntimeNativeArchiveValidationRequest(
    string Target,
    string ArchivePath,
    string OutputPath);
