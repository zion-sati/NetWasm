using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;

namespace NetWasm.Runtime.Pack.Materialization;

internal sealed class RuntimeModuleMaterializer(
    IRuntimePackManifestReader manifests,
    IRuntimeLayoutReader layouts,
    IRuntimeMemoryLayoutCalculator memoryLayouts,
    IRuntimeAssetDigestVerifier assetDigests,
    IRuntimeLinkArgumentBuilder linkArguments,
    ICommandInvoker commands,
    IArtifactDigestCalculator artifactDigests) : IRuntimeModuleMaterializer
{
    private const string DisableExperimentalWarningOption =
        "--disable-warning=ExperimentalWarning";

    public RuntimeMaterialization Materialize(RuntimeMaterializationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateRequest(request);
        var manifest = manifests.Read(request.ManifestPath);
        var sourceLayout = layouts.Read(request.RuntimeLayoutPath);
        if (!string.Equals(sourceLayout.Target, request.Target, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The NetWasm runtime layout target does not match the requested target.");
        }

        var target = manifest.Targets.SingleOrDefault(
            target => string.Equals(target.Target, request.Target, StringComparison.Ordinal))
            ?? throw new InvalidOperationException("The requested NetWasm runtime target is unavailable.");
        VerifyAssets(request.AssetRoot, target);
        var memoryLayout = memoryLayouts.Calculate(new(
            target,
            manifest.WasmPageSize,
            sourceLayout.ApplicationStaticDataEnd,
            request.InitialHeapSizeBytes,
            request.MaximumMemorySizeBytes));
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(request.OutputPath))!);
        Directory.CreateDirectory(request.LogDirectory);
        var arguments = linkArguments.Build(new(
            manifest,
            target,
            memoryLayout,
            request.AssetRoot,
            request.OutputPath));
        commands.Invoke(new(
            request.WasmLdPath,
            arguments,
            Path.Combine(request.LogDirectory, "runtime-link.log")));
        commands.Invoke(new(
            request.WasmToolsNodePath,
            ImmutableArray.Create(
                DisableExperimentalWarningOption,
                request.WasmToolsCommandPath,
                request.WasmToolsModulePath,
                "validate",
                Path.GetFullPath(request.OutputPath),
                "--features",
                "all"),
            Path.Combine(request.LogDirectory, "runtime-validate.log")));

        return new RuntimeMaterialization(
            target.Target,
            Path.GetFullPath(request.OutputPath),
            artifactDigests.Calculate(request.OutputPath),
            manifest.RuntimeAbi,
            manifest.Provenance.BuildSeam,
            manifest.Provenance.ToolchainFingerprint,
            memoryLayout.RuntimeGlobalBase,
            memoryLayout.HeapBase,
            memoryLayout.InitialMemorySizeBytes,
            memoryLayout.MaximumMemorySizeBytes);
    }

    private static void ValidateRequest(RuntimeMaterializationRequest request)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.ManifestPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.RuntimeLayoutPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.AssetRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.WasmLdPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.WasmToolsNodePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.WasmToolsCommandPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.WasmToolsModulePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.OutputPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.LogDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Target);
    }

    private void VerifyAssets(string assetRoot, RuntimePackTarget target)
    {
        assetDigests.Verify(ResolveAsset(assetRoot, target.RuntimeArchive.Path), target.RuntimeArchive.Sha256);
        foreach (var input in target.LinkInputs)
        {
            assetDigests.Verify(ResolveAsset(assetRoot, input.Path), input.Sha256);
        }
    }

    private static string ResolveAsset(string assetRoot, string relativePath) =>
        Path.GetFullPath(Path.Combine(assetRoot, relativePath));
}
