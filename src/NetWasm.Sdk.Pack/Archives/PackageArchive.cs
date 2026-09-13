using System.Collections.Immutable;

namespace NetWasm.Sdk.Pack.Archives;

public sealed record PackageArchive(
    PackageIdentity Identity,
    ImmutableArray<ArchiveEntry> Entries,
    DeterminismPolicy Policy);
