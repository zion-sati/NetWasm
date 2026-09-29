using NetWasm.Runtime.Pack.Materialization;

namespace NetWasm.Runtime.Pack.Composition;

internal static class RuntimeMaterializationComposition
{
    public static IRuntimeModuleMaterializer Create() =>
        new RuntimeModuleMaterializer(
            new RuntimePackManifestReader(),
            new RuntimeLayoutReader(),
            new RuntimeMemoryLayoutCalculator(),
            new RuntimeAssetDigestVerifier(),
            new RuntimeLinkArgumentBuilder(),
            new RuntimeOptimizationArgumentBuilder(),
            new RuntimeMaterializationCacheKeyBuilder(),
            new RuntimeMaterializationCacheReader(),
            new RuntimeMaterializationCacheWriter(),
            new RuntimeArtifactPublisher(new Sha256ArtifactDigestCalculator()),
            new CommandInvoker(),
            new Sha256ArtifactDigestCalculator());
}
