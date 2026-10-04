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

    public RuntimeMaterializationCacheKey Build(RuntimeNativeMaterializationCacheKeyRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Plan);
        ArgumentNullException.ThrowIfNull(request.Target.NativeValidation);
        var hasCallbacks = request.NativeCallbackSupport is not null;
        if (request.Bindings.IsDefault ||
            request.Bindings.IsEmpty && !hasCallbacks ||
            hasCallbacks != !string.IsNullOrWhiteSpace(request.NativeCallbackObjectPath) ||
            hasCallbacks != !string.IsNullOrWhiteSpace(
                request.NativeCallbackAllowedUndefinedPath))
            throw new InvalidOperationException(
                "A native materialization cache key requires selected bindings or callback support.");
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Append(hash, "schema", SchemaVersion);
        Append(hash, "mode", "static-native-scalar-v1");
        var profile = request.Target.NativeValidation;
        Append(hash, "validation.version", profile.Version);
        Append(hash, "validation.features.count", profile.Features.Length);
        for (var index = 0; index < profile.Features.Length; index++)
            Append(hash, $"validation.feature.{index}", profile.Features[index]);
        Append(hash, "validation.imports.count", profile.Imports.Length);
        for (var index = 0; index < profile.Imports.Length; index++)
        {
            var import = profile.Imports[index];
            var prefix = $"validation.import.{index}";
            Append(hash, prefix + ".module", import.Module);
            Append(hash, prefix + ".name", import.Name);
            Append(hash, prefix + ".required", import.Required ? 1 : 0);
            Append(hash, prefix + ".parameters", Convert.ToHexString(import.Parameters.AsSpan()));
            Append(hash, prefix + ".results", Convert.ToHexString(import.Results.AsSpan()));
        }
        AppendIdentity(hash, request.BuildIdentity);
        AppendManifest(hash, request.Manifest);
        AppendTarget(hash, request.Target);
        var plan = request.Plan;
        Append(hash, "plan.target", plan.Target);
        Append(hash, "plan.pointerSize", plan.PointerSizeBytes);
        Append(hash, "plan.pageSize", plan.WasmPageSize);
        Append(hash, "plan.alignment", plan.Alignment);
        Append(hash, "plan.applicationEnd", plan.ApplicationStaticDataEnd);
        Append(hash, "plan.globalBase", plan.RuntimeGlobalBase);
        Append(hash, "plan.initialHeap", plan.InitialHeapSizeBytes);
        Append(hash, "plan.maximumMemory", plan.MaximumMemorySizeBytes);
        Append(hash, "plan.nativeStack", plan.NativeStackSizeBytes);
        Append(hash, "native.count", request.Bindings.Length);
        for (var index = 0; index < request.Bindings.Length; index++)
        {
            var binding = request.Bindings[index];
            var prefix = $"native.{index}";
            Append(hash, prefix + ".library", binding.Import.LibraryName);
            Append(hash, prefix + ".entry", binding.Import.EntryPoint);
            Append(hash, prefix + ".target", binding.Provider.Target);
            Append(hash, prefix + ".path", binding.Provider.Path);
            Append(hash, prefix + ".digest", binding.Provider.Sha256);
            Append(hash, prefix + ".parameters", binding.Import.Parameters.Length);
            for (var parameter = 0; parameter < binding.Import.Parameters.Length; parameter++)
                Append(hash, prefix + $".parameter.{parameter}", binding.Import.Parameters[parameter].ToString());
            Append(hash, prefix + ".return", binding.Import.ReturnType?.ToString() ?? "void");
        }
        AppendCallbacks(hash, request.NativeCallbackSupport);
        Append(hash, "optimization", request.Optimization.ToString());
        AppendArguments(
            hash,
            "link",
            request.LinkArguments,
            request.AssetRoot,
            request.OutputPath,
            request.NativeCallbackObjectPath,
            request.NativeCallbackAllowedUndefinedPath);
        AppendArguments(hash, "optimization", request.OptimizationArguments, request.AssetRoot, request.OutputPath);
        return new(Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant());
    }

    private static void AppendCallbacks(
        IncrementalHash hash,
        RuntimeNativeCallbackSupport? support)
    {
        Append(hash, "callbacks.present", support is null ? 0 : 1);
        if (support is null)
            return;
        Append(hash, "callbacks.file", support.FileName);
        Append(hash, "callbacks.digest", support.Sha256);
        Append(hash, "callbacks.count", support.Callbacks.Length);
        for (var index = 0; index < support.Callbacks.Length; index++)
        {
            var callback = support.Callbacks[index];
            var prefix = $"callbacks.{index}";
            Append(hash, prefix + ".native", callback.NativeSymbol);
            Append(hash, prefix + ".runtimeImport", callback.RuntimeImportSymbol);
            Append(hash, prefix + ".application", callback.ApplicationExportName);
            Append(hash, prefix + ".getter", callback.RuntimeGetterExportName ?? "none");
            Append(hash, prefix + ".parameters", callback.Parameters.Length);
            for (var parameter = 0; parameter < callback.Parameters.Length; parameter++)
                Append(hash, prefix + $".parameter.{parameter}",
                    callback.Parameters[parameter].ToString());
            Append(hash, prefix + ".return", callback.ReturnType?.ToString() ?? "void");
        }
        Append(hash, "callbacks.applicationExports.count",
            support.TemporaryApplicationExports.Length);
        for (var index = 0; index < support.TemporaryApplicationExports.Length; index++)
            Append(hash, $"callbacks.applicationExports.{index}",
                support.TemporaryApplicationExports[index]);
        Append(hash, "callbacks.runtimeExports.count",
            support.TemporaryRuntimeExports.Length);
        for (var index = 0; index < support.TemporaryRuntimeExports.Length; index++)
            Append(hash, $"callbacks.runtimeExports.{index}",
                support.TemporaryRuntimeExports[index]);
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
        string outputPath,
        string? callbackObjectPath = null,
        string? callbackAllowedUndefinedPath = null)
    {
        Append(hash, $"arguments.{name}.count", arguments.Length);
        for (var index = 0; index < arguments.Length; index++)
        {
            Append(
                hash,
                $"arguments.{name}.{index}",
                NormalizeArgument(
                    arguments[index],
                    assetRoot,
                    outputPath,
                    callbackObjectPath,
                    callbackAllowedUndefinedPath));
        }
    }

    private static string NormalizeArgument(
        string argument,
        string assetRoot,
        string outputPath,
        string? callbackObjectPath = null,
        string? callbackAllowedUndefinedPath = null)
    {
        var fullOutputPath = Path.GetFullPath(outputPath);
        var fullAssetRoot = Path.GetFullPath(assetRoot)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var assetPrefix = fullAssetRoot + Path.DirectorySeparatorChar;
        var normalized = argument
            .Replace(fullOutputPath, "$output", StringComparison.Ordinal)
            .Replace(assetPrefix, "$assets/", StringComparison.Ordinal);
        if (!string.IsNullOrWhiteSpace(callbackObjectPath))
            normalized = normalized.Replace(
                Path.GetFullPath(callbackObjectPath),
                "$callback-object",
                StringComparison.Ordinal);
        if (!string.IsNullOrWhiteSpace(callbackAllowedUndefinedPath))
            normalized = normalized.Replace(
                Path.GetFullPath(callbackAllowedUndefinedPath),
                "$callback-undefined",
                StringComparison.Ordinal);
        return normalized.Replace('\\', '/');
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
