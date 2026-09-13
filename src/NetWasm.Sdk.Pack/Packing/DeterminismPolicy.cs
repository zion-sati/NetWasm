namespace NetWasm.Sdk.Pack.Packing;

public sealed record DeterminismPolicy(
    DateTimeOffset EntryTimestamp,
    CompressionMode Compression,
    int CompressionLevel,
    bool Utf8Names,
    int MaxEntries = 10_000,
    long MaxEntryBytes = 512L * 1024 * 1024,
    long MaxArchiveBytes = 2L * 1024 * 1024 * 1024)
{
    public static DeterminismPolicy Default { get; } = new(
        new DateTimeOffset(1980, 1, 1, 0, 0, 0, TimeSpan.Zero),
        CompressionMode.Optimal,
        6,
        true);
}
