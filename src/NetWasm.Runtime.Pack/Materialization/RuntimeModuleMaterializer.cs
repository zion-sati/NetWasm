using System;
using System.Collections.Immutable;
using System.Diagnostics;
using System.IO;
using System.Linq;
using NetWasm.Runtime.Pack.Planning;

namespace NetWasm.Runtime.Pack.Materialization;

internal sealed class RuntimeModuleMaterializer(
    IRuntimePackManifestReader manifests,
    IRuntimeLayoutReader layouts,
    IRuntimeMemoryLayoutCalculator memoryLayouts,
    IRuntimeAssetDigestVerifier assetDigests,
    IRuntimeLinkArgumentBuilder linkArguments,
    IRuntimeOptimizationArgumentBuilder optimizationArguments,
    IRuntimeMaterializationCacheKeyBuilder cacheKeys,
    IRuntimeMaterializationCacheReader cacheReader,
    IRuntimeMaterializationCacheWriter cacheWriter,
    IRuntimeArtifactPublisher artifactPublisher,
    ICommandInvoker commands,
    IArtifactDigestCalculator artifactDigests) : IRuntimeModuleMaterializer
{
    private const string DisableExperimentalWarningOption =
        "--disable-warning=ExperimentalWarning";

    public RuntimeMaterialization Materialize(RuntimeMaterializationRequest request)
    {
        var totalStarted = Stopwatch.GetTimestamp();
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
        var systemLibraryPaths = target.SystemLibraries.Assets
            .Select(asset => ResolveAsset(request.AssetRoot, asset.Path))
            .ToImmutableArray();
        VerifyAssets(request.AssetRoot, target);
        var memoryLayout = memoryLayouts.Calculate(new(
            target,
            manifest.WasmPageSize,
            sourceLayout.ApplicationStaticDataEnd,
            request.InitialHeapSizeBytes,
            request.MaximumMemorySizeBytes));
        var arguments = linkArguments.Build(new(
            manifest,
            target,
            memoryLayout,
            request.AssetRoot,
            systemLibraryPaths,
            request.OutputPath));
        var optimizeArguments = request.Optimization == RuntimeWasmOptimization.None
            ? ImmutableArray<string>.Empty
            : optimizationArguments.Build(new(target, request.OutputPath, request.Optimization));
        var cacheKey = cacheKeys.Build(new(
            request.BuildIdentity,
            manifest,
            target,
            memoryLayout,
            request.Optimization,
            arguments,
            optimizeArguments,
            request.AssetRoot,
            request.OutputPath));
        var cacheSlot = new RuntimeMaterializationCacheSlot(target.Target, request.Optimization);
        var lookupStarted = Stopwatch.GetTimestamp();
        var cached = cacheReader.Read(request.CacheDirectory, cacheSlot, cacheKey);
        var lookupMilliseconds = Stopwatch.GetElapsedTime(lookupStarted).TotalMilliseconds;
        if (cached.Outcome == RuntimeMaterializationCacheOutcome.Hit)
        {
            artifactPublisher.PublishIfDifferent(request.OutputPath, cached.Bytes!, cached.Sha256!);
            return CreateResult(
                request,
                manifest,
                target,
                memoryLayout,
                cached.Sha256!,
                new(
                    "runtime-materialization",
                    cacheKey.Prefix,
                    cached.Outcome,
                    Recomputed: false,
                    cached.Bytes!.LongLength,
                    lookupMilliseconds,
                    Stopwatch.GetElapsedTime(totalStarted).TotalMilliseconds));
        }

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(request.OutputPath))!);
        Directory.CreateDirectory(request.LogDirectory);
        commands.Invoke(new(
            request.WasmLdPath,
            arguments,
            Path.Combine(request.LogDirectory, "runtime-link.log")));
        if (request.Optimization != RuntimeWasmOptimization.None)
        {
            commands.Invoke(new(
                request.WasmOptPath,
                optimizeArguments,
                Path.Combine(request.LogDirectory, "runtime-optimize.log")));
        }
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

        var sha256 = artifactDigests.Calculate(request.OutputPath);
        var bytes = File.ReadAllBytes(request.OutputPath);
        try
        {
            cacheWriter.Write(request.CacheDirectory, cacheSlot, cacheKey, bytes, sha256);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            // Runtime cache persistence is opportunistic. The validated output
            // remains the authoritative result when optional storage is unavailable.
        }
        return CreateResult(
            request,
            manifest,
            target,
            memoryLayout,
            sha256,
            new(
                "runtime-materialization",
                cacheKey.Prefix,
                cached.Outcome,
                Recomputed: true,
                bytes.LongLength,
                lookupMilliseconds,
                Stopwatch.GetElapsedTime(totalStarted).TotalMilliseconds));
    }

    private static RuntimeMaterialization CreateResult(
        RuntimeMaterializationRequest request,
        RuntimePackManifest manifest,
        RuntimePackTarget target,
        RuntimeMemoryLayout memoryLayout,
        string sha256,
        RuntimeMaterializationCacheMetrics metrics) =>
        new(
            target.Target,
            Path.GetFullPath(request.OutputPath),
            sha256,
            manifest.RuntimeAbi,
            manifest.Provenance.BuildSeam,
            manifest.Provenance.ToolchainFingerprint,
            memoryLayout.RuntimeGlobalBase,
            memoryLayout.HeapBase,
            memoryLayout.InitialMemorySizeBytes,
            memoryLayout.MaximumMemorySizeBytes,
            metrics);

    private static void ValidateRequest(RuntimeMaterializationRequest request)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.ManifestPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.RuntimeLayoutPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.AssetRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.WasmLdPath);
        if (!Enum.IsDefined(request.Optimization))
        {
            throw new ArgumentOutOfRangeException(nameof(request));
        }
        if (request.Optimization != RuntimeWasmOptimization.None)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(request.WasmOptPath);
        }
        ArgumentException.ThrowIfNullOrWhiteSpace(request.WasmToolsNodePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.WasmToolsCommandPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.WasmToolsModulePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.OutputPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.LogDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.CacheDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Target);
        ValidateBuildIdentity(request.BuildIdentity);
    }

    private static void ValidateBuildIdentity(RuntimeBuildIdentity identity)
    {
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentException.ThrowIfNullOrWhiteSpace(identity.SdkVersion);
        ArgumentException.ThrowIfNullOrWhiteSpace(identity.CompilerVersion);
        ArgumentException.ThrowIfNullOrWhiteSpace(identity.RuntimeVersion);
        ArgumentException.ThrowIfNullOrWhiteSpace(identity.RuntimePackVersion);
        ArgumentException.ThrowIfNullOrWhiteSpace(identity.HostToolsPackageId);
        ArgumentException.ThrowIfNullOrWhiteSpace(identity.HostToolsPackageVersion);
        ArgumentException.ThrowIfNullOrWhiteSpace(identity.WasmLdVersion);
        ArgumentException.ThrowIfNullOrWhiteSpace(identity.WasmOptVersion);
        ArgumentException.ThrowIfNullOrWhiteSpace(identity.WasmToolsVersion);
        ArgumentException.ThrowIfNullOrWhiteSpace(identity.NodeVersion);
    }

    private void VerifyAssets(string assetRoot, RuntimePackTarget target)
    {
        assetDigests.Verify(ResolveAsset(assetRoot, target.RuntimeArchive.Path), target.RuntimeArchive.Sha256);
        assetDigests.Verify(ResolveAsset(assetRoot, target.CollectorArchive.Path), target.CollectorArchive.Sha256);
        assetDigests.Verify(
            ResolveAsset(assetRoot, target.AllowedUndefinedSymbols.Path),
            target.AllowedUndefinedSymbols.Sha256);
        if (target.SystemLibraries.Assets.Length != target.SystemLibraries.Names.Length)
        {
            throw new InvalidOperationException("The packaged Emscripten system-library closure is incomplete.");
        }

        for (var index = 0; index < target.SystemLibraries.Assets.Length; index++)
        {
            var asset = target.SystemLibraries.Assets[index];
            assetDigests.Verify(ResolveAsset(assetRoot, asset.Path), asset.Sha256);
        }
    }

    private static string ResolveAsset(string assetRoot, string relativePath) =>
        Path.GetFullPath(Path.Combine(assetRoot, relativePath));

}
