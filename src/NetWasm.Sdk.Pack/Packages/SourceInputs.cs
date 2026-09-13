using System.Collections.Immutable;

namespace NetWasm.Sdk.Pack.Packages;

public sealed record SourceInputs(
    bool IncludeSource,
    ImmutableArray<SourceInput> Files,
    bool PublishRepositoryUrl,
    string? SourceLinkJson = null)
{
    public static SourceInputs None { get; } = new(false, ImmutableArray<SourceInput>.Empty, false);
}
