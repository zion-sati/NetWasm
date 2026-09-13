using System.Collections.Immutable;

namespace NetWasm.Sdk.Pack.Archives;

public sealed record ArchivePlan(
    string OutputPath,
    ImmutableArray<ArchiveEntry> Entries,
    DeterminismPolicy Policy,
    PackageIdentity Identity);
