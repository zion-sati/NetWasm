using System;
using System.Buffers.Binary;
using System.Collections.Immutable;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace NetWasm.Runtime.Pack.Materialization;

internal sealed class RuntimeMaterializationCacheKeyBuilder :
    IRuntimeMaterializationCacheKeyBuilder
{
    private const int SchemaVersion = 1;

    public RuntimeMaterializationCacheKey Build(
        RuntimeMaterializationCacheKeyRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

        Append(hash, "schema", SchemaVersion);
        AppendIdentity(hash, request.BuildIdentity);
        AppendManifest(hash, request.Manifest);
        AppendTarget(hash, request.Target);
        Append(hash, "layout.runtimeGlobalBase", request.Layout.RuntimeGlobalBase);
        Append(hash, "layout.heapBase", request.Layout.HeapBase);
        Append(hash, "layout.initialMemory", request.Layout.InitialMemorySizeBytes);
        Append(hash, "layout.maximumMemory", request.Layout.MaximumMemorySizeBytes);
        Append(hash, "optimization", request.Optimization.ToString());
        AppendArguments(hash, "link", request.LinkArguments, request.AssetRoot, request.OutputPath);
        AppendArguments(
            hash,
            "optimization",
            request.OptimizationArguments,
            request.AssetRoot,
            request.OutputPath);

        return new(Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant());
    }

    private static void AppendIdentity(IncrementalHash hash, RuntimeBuildIdentity identity)
    {
        ArgumentNullException.ThrowIfNull(identity);
        Append(hash, "identity.sdk", identity.SdkVersion);
        Append(hash, "identity.compiler", identity.CompilerVersion);
        Append(hash, "identity.runtime", identity.RuntimeVersion);
        Append(hash, "identity.runtimePack", identity.RuntimePackVersion);
        Append(hash, "identity.hostToolsPackageId", identity.HostToolsPackageId);
        Append(hash, "identity.hostToolsPackageVersion", identity.HostToolsPackageVersion);
        Append(hash, "identity.wasmLd", identity.WasmLdVersion);
        Append(hash, "identity.wasmOpt", identity.WasmOptVersion);
        Append(hash, "identity.wasmTools", identity.WasmToolsVersion);
        Append(hash, "identity.node", identity.NodeVersion);
    }

    private static void AppendManifest(IncrementalHash hash, RuntimePackManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        Append(hash, "manifest.schema", manifest.SchemaVersion);
        Append(hash, "manifest.runtimeAbi", manifest.RuntimeAbi);
        Append(hash, "manifest.emscripten", manifest.EmscriptenVersion);
        Append(hash, "manifest.wasmPageSize", manifest.WasmPageSize);
        Append(hash, "manifest.exports.count", manifest.Exports.Length);
        for (var index = 0; index < manifest.Exports.Length; index++)
        {
            Append(hash, $"manifest.exports.{index}", manifest.Exports[index]);
        }

        Append(hash, "manifest.provenance.buildSeam", manifest.Provenance.BuildSeam);
        Append(hash, "manifest.provenance.configuration", manifest.Provenance.Configuration);
        Append(hash, "manifest.provenance.toolchainFingerprint", manifest.Provenance.ToolchainFingerprint);
    }

    private static void AppendTarget(IncrementalHash hash, RuntimePackTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);
        Append(hash, "target.name", target.Target);
        Append(hash, "target.pointerSize", target.PointerSizeBytes);
        Append(hash, "target.alignment", target.Alignment);
        Append(hash, "target.runtimeFootprint", target.RuntimeFootprintBytes);
        Append(hash, "target.nativeStack", target.NativeStackSizeBytes);
        Append(hash, "target.defaultInitialHeap", target.DefaultInitialHeapSizeBytes);
        Append(hash, "target.defaultMaximumMemory", target.DefaultMaximumMemorySizeBytes);
        Append(hash, "target.maximumMemory", target.MaximumMemorySizeBytes);
        AppendAsset(hash, "target.runtimeArchive", target.RuntimeArchive);
        AppendAsset(hash, "target.collectorArchive", target.CollectorArchive);
        AppendAsset(hash, "target.allowedUndefined", target.AllowedUndefinedSymbols);
        Append(hash, "target.systemLibraries.count", target.SystemLibraries.Assets.Length);
        for (var index = 0; index < target.SystemLibraries.Assets.Length; index++)
        {
            Append(hash, $"target.systemLibraries.{index}.name", target.SystemLibraries.Names[index]);
            AppendAsset(hash, $"target.systemLibraries.{index}.asset", target.SystemLibraries.Assets[index]);
        }
    }

    private static void AppendAsset(IncrementalHash hash, string name, RuntimePackAsset asset)
    {
        Append(hash, $"{name}.path", asset.Path.Replace('\\', '/'));
        Append(hash, $"{name}.sha256", asset.Sha256.ToLowerInvariant());
    }

    private static void AppendArguments(
        IncrementalHash hash,
        string name,
        ImmutableArray<string> arguments,
        string assetRoot,
        string outputPath)
    {
        Append(hash, $"arguments.{name}.count", arguments.Length);
        for (var index = 0; index < arguments.Length; index++)
        {
            Append(
                hash,
                $"arguments.{name}.{index}",
                NormalizeArgument(arguments[index], assetRoot, outputPath));
        }
    }

    private static string NormalizeArgument(string argument, string assetRoot, string outputPath)
    {
        var fullOutputPath = Path.GetFullPath(outputPath);
        var fullAssetRoot = Path.GetFullPath(assetRoot)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var assetPrefix = fullAssetRoot + Path.DirectorySeparatorChar;
        return argument
            .Replace(fullOutputPath, "$output", StringComparison.Ordinal)
            .Replace(assetPrefix, "$assets/", StringComparison.Ordinal)
            .Replace('\\', '/');
    }

    private static void Append(IncrementalHash hash, string name, int value) =>
        Append(hash, name, value.ToString(System.Globalization.CultureInfo.InvariantCulture));

    private static void Append(IncrementalHash hash, string name, long value) =>
        Append(hash, name, value.ToString(System.Globalization.CultureInfo.InvariantCulture));

    private static void Append(IncrementalHash hash, string name, string value)
    {
        AppendBytes(hash, Encoding.UTF8.GetBytes(name));
        AppendBytes(hash, Encoding.UTF8.GetBytes(value));
    }

    private static void AppendBytes(IncrementalHash hash, byte[] bytes)
    {
        Span<byte> length = stackalloc byte[sizeof(int)];
        BinaryPrimitives.WriteInt32LittleEndian(length, bytes.Length);
        hash.AppendData(length);
        hash.AppendData(bytes);
    }
}
