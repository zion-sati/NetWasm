using System;
using System.Collections.Generic;

namespace NetWasm.Compiler;

public sealed record FrontendArtifactCacheDescriptor(
    string Schema,
    string Namespace,
    string PreparationToken);

public sealed record FrontendArtifactCacheEntry(string Key, byte[] Payload, byte[] Checksum);

public sealed record FrontendArtifactCachePublication(
    string Token,
    int EntryCount,
    long TotalBytes);

public sealed record FrontendArtifactCacheBatch(
    string PublicationToken,
    string BatchToken,
    IReadOnlyList<FrontendArtifactCacheEntry> Entries,
    bool IsFinal);

public interface IFrontendArtifactCacheTransport
{
    FrontendArtifactCacheDescriptor? Prepare(CompilerOptions options);

    IDisposable BeginCompilation(
        FrontendArtifactCacheDescriptor descriptor,
        IReadOnlyList<FrontendArtifactCacheEntry> entries);

    void CancelPreparation(FrontendArtifactCacheDescriptor descriptor);

    FrontendArtifactCachePublication? CompleteCompilation(
        FrontendArtifactCacheDescriptor descriptor);

    FrontendArtifactCacheBatch ReadBatch(FrontendArtifactCachePublication publication);

    void AcknowledgeBatch(
        FrontendArtifactCachePublication publication,
        FrontendArtifactCacheBatch batch);

    void Abandon(FrontendArtifactCachePublication publication);
}
