using NetWasm.Runtime.Pack.Materialization;
using NetWasm.Runtime.Pack.MsBuild;

namespace NetWasm.Runtime.Pack.Composition;

internal static class RuntimeMaterializationComposition
{
    public static INativeLibraryItemReader CreateNativeLibraryItemReader() => new NativeLibraryItemReader();

    public static IRuntimeModuleMaterializer Create()
    {
        var writer = new RuntimeArtifactWriter();
        return new RuntimeModuleMaterializer(
            new RuntimePackManifestReader(new RuntimePackManifestParser()),
            new RuntimePackTargetSelector(new RuntimeOptimizationPolicyResolver()),
            new RuntimeLayoutReader(),
            new RuntimeMemoryLayoutCalculator(new RuntimeMemoryPlanBuilder()),
            new RuntimeAssetDigestVerifier(),
            new RuntimeCollectorCompatibilityValidator(),
            new RuntimeLinkArgumentBuilder(new RuntimeLinkExportPlanBuilder()),
            new RuntimeOptimizationArgumentBuilder(new RuntimeOptimizationPolicyResolver()),
            new RuntimeMaterializationCacheKeyBuilder(),
            new RuntimeMaterializationCacheReader(),
            new RuntimeMaterializationCacheWriter(),
            new RuntimeArtifactPublisher(new Sha256ArtifactDigestCalculator(), writer),
            writer,
            new RuntimeInlineIntentWriter(),
            new CommandInvoker(),
            new Sha256ArtifactDigestCalculator(),
            CreateNativeMaterializer(),
            new ResponseFileLinkerCommandFactory());
    }

    public static IRuntimeNativeModuleMaterializer CreateNativeMaterializer()
    {
        var writer = new RuntimeArtifactWriter();
        return new RuntimeNativeModuleMaterializer(
            new RuntimeNativeLibraryResolver(new RuntimeNativeArchiveReader()),
            new RuntimeNativeProviderSelector(),
            new RuntimeMemoryPlanBuilder(),
            new RuntimeNativeArchiveValidationArgumentBuilder(),
            new RuntimeLinkArgumentBuilder(new RuntimeLinkExportPlanBuilder()),
            new RuntimeOptimizationArgumentBuilder(new RuntimeOptimizationPolicyResolver()),
            new LinkedRuntimeModuleReader(),
            new LinkedMemoryLayoutCalculator(new LinkedMemoryLayoutValidator()),
            new LinkedMemoryLayoutValidator(),
            new LinkerSymbolTraceReader(),
            new RuntimeNativeBindingValidator(),
            new RuntimeMaterializationCacheKeyBuilder(),
            new RuntimeMaterializationCacheReader(),
            new RuntimeMaterializationCacheWriter(),
            new RuntimeArtifactPublisher(new Sha256ArtifactDigestCalculator(), writer),
            writer,
            new RuntimeInlineIntentWriter(),
            new RuntimeArtifactReader(),
            new RuntimeNativeArchiveSnapshotter(new RuntimeNativeArchiveReader()),
            new CommandInvoker(),
            new RuntimeNativeLinkWorkspaceFactory(),
            new RuntimeLinkExportPlanBuilder(),
            new RuntimeLinkedImportValidator(new RuntimeNativeValidationProfileValidator()),
            new RuntimeNativeModuleValidator(new RuntimeNativeValidationArgumentBuilder(new RuntimeNativeValidationProfileValidator()), new CommandInvoker()),
            new RuntimeNativeValidationProfileValidator(),
            new RuntimeNativeArchiveFeatureValidator(
                new RuntimeNativeModuleValidator(new RuntimeNativeValidationArgumentBuilder(new RuntimeNativeValidationProfileValidator()), new CommandInvoker()),
                new RuntimeArtifactReader()),
            new ResponseFileLinkerCommandFactory());
    }
}
