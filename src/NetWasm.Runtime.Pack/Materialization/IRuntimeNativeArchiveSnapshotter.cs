using System.Collections.Immutable;

namespace NetWasm.Runtime.Pack.Materialization;

internal interface IRuntimeNativeArchiveSnapshotter
{
    ImmutableArray<RuntimeNativeArchiveSnapshot> Snapshot(RuntimeNativeArchiveSnapshotRequest request);
}

internal sealed record RuntimeNativeArchiveSnapshotRequest(
    ImmutableArray<RuntimeNativeLibrary> Providers, string DirectoryPath);

internal sealed record RuntimeNativeArchiveSnapshot(RuntimeNativeLibrary Provider, string SnapshotPath);
