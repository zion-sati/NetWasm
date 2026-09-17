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
        var target = manifest.Targets.SingleOrDefault(item => item.Target == request.Target)
            ?? throw new InvalidOperationException("The requested NetWasm runtime target is unavailable.");
        var layout = new RuntimeMemoryLayoutCalculator().Calculate(new(
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
                request.SystemLibraries[index].Sha256.Length != 64 ||
                !request.SystemLibraries[index].Sha256.All(Uri.IsHexDigit))
            {
                throw new InvalidOperationException("The resolved Emscripten system-library closure is invalid.");
            }
        }

        var systemLibraryPaths = request.SystemLibraries.Select(asset => asset.Path).ToImmutableArray();
        var link = new RuntimeLinkRequest(
            manifest, target, layout, request.AssetRoot, systemLibraryPaths, request.OutputPath);
        var arguments = new RuntimeLinkArgumentBuilder().Build(link);
        var runtimeInput = new RuntimeLinkPlanAsset(
            request.AssetRoot.TrimEnd('/') + "/" + target.RuntimeArchive.Path.Replace('\\', '/'),
            target.RuntimeArchive.Sha256);
        var inputs = ImmutableArray.Create(runtimeInput).AddRange(request.SystemLibraries);
        if (inputs.Any(asset => asset.Path == request.OutputPath))
        {
            throw new ArgumentException("The runtime output cannot overwrite a runtime input.", nameof(request));
        }

        // The existing builder canonicalizes paths using the host platform.
        // Map only those path values back to MEMFS names; every flag/order is unchanged.
        var paths = new Dictionary<string, string>(StringComparer.Ordinal);
        paths[Path.GetFullPath(Path.Combine(Path.GetFullPath(request.AssetRoot), target.RuntimeArchive.Path))] = runtimeInput.Path;
        foreach (var systemLibrary in request.SystemLibraries)
        {
            paths[Path.GetFullPath(systemLibrary.Path)] = systemLibrary.Path;
        }
        paths[Path.GetFullPath(request.OutputPath)] = request.OutputPath;
        var virtualArguments = arguments.Select(argument => paths.TryGetValue(argument, out var path) ? path : argument)
            .ToImmutableArray();
        return new(virtualArguments, inputs, manifest.RuntimeAbi, manifest.Provenance.ToolchainFingerprint,
            layout.RuntimeGlobalBase, layout.HeapBase, layout.InitialMemorySizeBytes, layout.MaximumMemorySizeBytes);
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
