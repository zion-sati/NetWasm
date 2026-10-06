using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using NetWasm.Runtime.Pack.Materialization;

namespace NetWasm.Runtime.Pack.Planning;

/// <summary>
/// Reuses desktop runtime memory and linker policy over manifest JSON.
/// Returned paths are absolute Unix virtual paths; planning opens no files.
/// </summary>
public static class RuntimeLinkPlanner
{
    public static RuntimeLinkPlan Plan(RuntimeLinkPlanRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateVirtualPath(request.AssetRoot);
        ValidateVirtualPath(request.OutputPath);
        var manifest = RuntimePackManifestReader.ReadJson(request.ManifestJson);
        if (!Enum.IsDefined(request.Optimization))
        {
            throw new ArgumentOutOfRangeException(nameof(request));
        }
        var target = new RuntimePackTargetSelector().Select(manifest, request.Target, request.GarbageCollector);
        var layout = new RuntimeMemoryLayoutCalculator(new RuntimeMemoryPlanBuilder()).Calculate(new(
            target, manifest.WasmPageSize, request.ApplicationStaticDataEnd,
            request.InitialHeapSizeBytes, request.MaximumMemorySizeBytes));
        if (request.SystemLibraries.IsDefault ||
            request.SystemLibraries.Length != target.SystemLibraries.Names.Length)
        {
            throw new InvalidOperationException("The resolved Emscripten system-library closure is incomplete.");
        }

        for (var index = 0; index < request.SystemLibraries.Length; index++)
        {
            ValidateVirtualPath(request.SystemLibraries[index].Path);
            if (!request.SystemLibraries[index].Path.EndsWith(
                    "/" + target.SystemLibraries.Names[index], StringComparison.Ordinal) ||
                !string.Equals(request.SystemLibraries[index].Sha256,
                    target.SystemLibraries.Assets[index].Sha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("The resolved Emscripten system-library closure is invalid.");
            }
        }

        var systemLibraryPaths = request.SystemLibraries.Select(asset => asset.Path).ToImmutableArray();
        var link = new RuntimeLinkRequest(
            manifest, target, new RuntimeLinkMemoryLimits(layout.RuntimeGlobalBase, layout.InitialMemorySizeBytes,
                layout.MaximumMemorySizeBytes), request.AssetRoot, systemLibraryPaths, request.OutputPath)
        {
            RuntimeFeatures = request.RuntimeFeatures,
        };
        var arguments = new RuntimeLinkArgumentBuilder(new RuntimeLinkExportPlanBuilder()).Build(link);
        var runtimeInput = new RuntimeLinkPlanAsset(
            request.AssetRoot.TrimEnd('/') + "/" + target.RuntimeArchive.Path.Replace('\\', '/'),
            target.RuntimeArchive.Sha256);
        var collectorInput = ResolveInput(request.AssetRoot, target.CollectorArchive);
        var allowedUndefinedInput = ResolveInput(
            request.AssetRoot, target.AllowedUndefinedSymbols);
        var inputs = ImmutableArray.Create(runtimeInput, collectorInput, allowedUndefinedInput)
            .AddRange(request.SystemLibraries);
        if (inputs.Any(asset => asset.Path == request.OutputPath))
        {
            throw new ArgumentException("The runtime output cannot overwrite a runtime input.", nameof(request));
        }

        // The existing builder canonicalizes paths using the host platform.
        // Map only those path values back to MEMFS names; every flag/order is unchanged.
        var paths = new Dictionary<string, string>(StringComparer.Ordinal);
        paths[Path.GetFullPath(Path.Combine(Path.GetFullPath(request.AssetRoot), target.RuntimeArchive.Path))] = runtimeInput.Path;
        paths[Path.GetFullPath(Path.Combine(
            Path.GetFullPath(request.AssetRoot), target.CollectorArchive.Path))] =
            collectorInput.Path;
        paths[Path.GetFullPath(Path.Combine(
            Path.GetFullPath(request.AssetRoot), target.AllowedUndefinedSymbols.Path))] =
            allowedUndefinedInput.Path;
        foreach (var systemLibrary in request.SystemLibraries)
        {
            paths[Path.GetFullPath(systemLibrary.Path)] = systemLibrary.Path;
        }
        paths[Path.GetFullPath(request.OutputPath)] = request.OutputPath;
        var virtualArguments = arguments.Select(argument => MapPathArgument(argument, paths))
            .ToImmutableArray();
        var optimizationArguments = new RuntimeOptimizationArgumentBuilder()
            .Build(new(target, request.OutputPath, request.Optimization)
            {
                RuntimeGlobalBase = layout.RuntimeGlobalBase,
            })
            .Select(argument => MapPathArgument(argument, paths))
            .ToImmutableArray();
        var cache = new RuntimeMaterializationCacheDescriptorBuilder().Build(new(
            target.Target,
            request.Optimization,
            manifest.RuntimeAbi,
            manifest.Provenance.ToolchainFingerprint,
            layout.RuntimeGlobalBase,
            layout.HeapBase,
            layout.InitialMemorySizeBytes,
            layout.MaximumMemorySizeBytes,
            virtualArguments,
            optimizationArguments,
            inputs));
        return new(virtualArguments, optimizationArguments, inputs, manifest.RuntimeAbi,
            manifest.Provenance.ToolchainFingerprint,
            layout.RuntimeGlobalBase, layout.HeapBase, layout.InitialMemorySizeBytes,
            layout.MaximumMemorySizeBytes, cache)
        {
            GarbageCollector = target.GarbageCollector,
        };
    }

    private static RuntimeLinkPlanAsset ResolveInput(
        string assetRoot,
        RuntimePackAsset asset) => new(
            assetRoot.TrimEnd('/') + "/" + asset.Path.Replace('\\', '/'),
            asset.Sha256);

    private static string MapPathArgument(
        string argument,
        Dictionary<string, string> paths)
    {
        if (paths.TryGetValue(argument, out var path))
        {
            return path;
        }

        const string allowedUndefinedPrefix = "--allow-undefined-file=";
        if (argument.StartsWith(allowedUndefinedPrefix, StringComparison.Ordinal) &&
            paths.TryGetValue(argument[allowedUndefinedPrefix.Length..], out path))
        {
            return allowedUndefinedPrefix + path;
        }

        return argument;
    }

    private static void ValidateVirtualPath(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (path[0] != '/' || path.Contains('\\') ||
            path.Split('/').Any(part => part is "." or "..") ||
            path.EndsWith('/'))
        {
            throw new ArgumentException("Runtime link paths must be absolute Unix virtual file paths.", nameof(path));
        }
    }
}
