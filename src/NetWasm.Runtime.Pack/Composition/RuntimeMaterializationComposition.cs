using NetWasm.Runtime.Pack.Materialization;
using NetWasm.Runtime.Pack.MsBuild;

namespace NetWasm.Runtime.Pack.Composition;

internal static class RuntimeMaterializationComposition
{
    public static INativeLibraryItemReader CreateNativeLibraryItemReader() => new NativeLibraryItemReader();

    public static IRuntimeModuleMaterializer Create() =>
        new RuntimeModuleMaterializer(
            new RuntimePackManifestReader(),
            new RuntimeLayoutReader(),
            new RuntimeMemoryLayoutCalculator(new RuntimeMemoryPlanBuilder()),
            new RuntimeAssetDigestVerifier(),
            new RuntimeLinkArgumentBuilder(new RuntimeLinkExportPlanBuilder()),
            new RuntimeOptimizationArgumentBuilder(),
            new RuntimeMaterializationCacheKeyBuilder(),
            new RuntimeMaterializationCacheReader(),
            new RuntimeMaterializationCacheWriter(),
            new RuntimeArtifactPublisher(new Sha256ArtifactDigestCalculator()),
            new CommandInvoker(),
            new Sha256ArtifactDigestCalculator(),
            CreateNativeMaterializer());

    public static IRuntimeNativeModuleMaterializer CreateNativeMaterializer() =>
        new RuntimeNativeModuleMaterializer(
            new RuntimeNativeLibraryResolver(new RuntimeNativeArchiveReader()),
            new RuntimeNativeProviderSelector(),
            new RuntimeMemoryPlanBuilder(),
            new RuntimeNativeArchiveValidationArgumentBuilder(),
            new RuntimeLinkArgumentBuilder(new RuntimeLinkExportPlanBuilder()),
            new RuntimeOptimizationArgumentBuilder(),
            new LinkedRuntimeModuleReader(),
            new LinkedMemoryLayoutCalculator(new LinkedMemoryLayoutValidator()),
            new LinkedMemoryLayoutValidator(),
            new LinkerSymbolTraceReader(),
            new RuntimeNativeBindingValidator(),
            new RuntimeMaterializationCacheKeyBuilder(),
            new RuntimeMaterializationCacheReader(),
            new RuntimeMaterializationCacheWriter(),
            new RuntimeArtifactPublisher(new Sha256ArtifactDigestCalculator()),
            new RuntimeArtifactReader(),
            new RuntimeNativeArchiveSnapshotter(new RuntimeNativeArchiveReader()),
            new CommandInvoker(),
            new RuntimeNativeLinkWorkspaceFactory(),
            new RuntimeLinkExportPlanBuilder(),
            new RuntimeLinkedImportValidator(new RuntimeNativeValidationProfileValidator()),
            new RuntimeNativeModuleValidator(new RuntimeNativeValidationArgumentBuilder(new RuntimeNativeValidationProfileValidator()), new CommandInvoker()),
            new RuntimeNativeValidationProfileValidator());
}
