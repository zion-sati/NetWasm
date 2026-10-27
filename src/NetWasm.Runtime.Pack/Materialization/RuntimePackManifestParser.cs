using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace NetWasm.Runtime.Pack.Materialization;

internal sealed class RuntimePackManifestParser : IRuntimePackManifestParser
{
    public RuntimePackManifest Parse(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        RuntimePackManifest? manifest;
        try
        {
            manifest = JsonSerializer.Deserialize(
                json,
                RuntimePackJsonContext.Default.RuntimePackManifest);
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException("The NetWasm runtime pack manifest is malformed.", exception);
        }

        Validate(manifest);
        return manifest! with
        {
            TargetLookup = manifest.Targets.ToImmutableDictionary(
                target => (target.Target, target.GarbageCollector)),
        };
    }

    private static void Validate(RuntimePackManifest? manifest)
    {
        if (manifest is null || manifest.SchemaVersion != 6 || manifest.WasmPageSize != 65_536)
        {
            throw new InvalidOperationException("The NetWasm runtime pack manifest schema is unsupported.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(manifest.RuntimeAbi);
        if (!Version.TryParse(manifest.EmscriptenVersion, out _))
        {
            throw new InvalidOperationException("The NetWasm runtime pack Emscripten requirement is invalid.");
        }
        ValidateProvenance(manifest.Provenance);
        if (manifest.Exports.IsDefaultOrEmpty ||
            manifest.Exports.Any(string.IsNullOrWhiteSpace) ||
            manifest.Exports.Distinct(StringComparer.Ordinal).Count() != manifest.Exports.Length)
        {
            throw new InvalidOperationException("The NetWasm runtime export contract is invalid.");
        }
        if (manifest.InlineIntents.IsDefault ||
            manifest.InlineIntents.Any(intent => intent is null ||
                string.IsNullOrWhiteSpace(intent.Export) ||
                !Enum.IsDefined(intent.Preference) ||
                !manifest.Exports.Contains(intent.Export, StringComparer.Ordinal)) ||
            manifest.InlineIntents.Select(intent => intent.Export)
                .Distinct(StringComparer.Ordinal).Count() != manifest.InlineIntents.Length)
        {
            throw new InvalidOperationException("The NetWasm runtime inline-intent contract is invalid.");
        }

        if (!RuntimeGarbageCollectors.IsSupported(manifest.DefaultGarbageCollector))
            throw new InvalidOperationException("The NetWasm runtime pack default collector is invalid.");

        if (manifest.Targets.IsDefault || manifest.Targets.Length != RuntimeGarbageCollectors.TargetCount ||
            manifest.Targets.Any(target => target is null) ||
            manifest.Targets.Select(static target => (target.Target, target.GarbageCollector)).Distinct().Count() !=
                RuntimeGarbageCollectors.TargetCount)
        {
            throw new InvalidOperationException("The NetWasm runtime target contract is invalid.");
        }

        foreach (var target in manifest.Targets)
        {
            ValidateTarget(target, manifest.WasmPageSize);
        }

    }

    private static void ValidateProvenance(RuntimePackProvenance provenance)
    {
        ArgumentNullException.ThrowIfNull(provenance);
        ArgumentException.ThrowIfNullOrWhiteSpace(provenance.BuildSeam);
        ArgumentException.ThrowIfNullOrWhiteSpace(provenance.Configuration);
        ArgumentException.ThrowIfNullOrWhiteSpace(provenance.ToolchainFile);
        ArgumentException.ThrowIfNullOrWhiteSpace(provenance.ToolchainFingerprint);
        ArgumentException.ThrowIfNullOrWhiteSpace(provenance.RuntimeSource);
        ArgumentException.ThrowIfNullOrWhiteSpace(provenance.LayoutAuthority);
    }

    private static void ValidateTarget(RuntimePackTarget target, long wasmPageSize)
    {
        if (!RuntimeGarbageCollectors.IsSupported(target.GarbageCollector))
            throw new InvalidOperationException("The NetWasm runtime target collector is unsupported.");
        var expectedPointerSize = target.Target switch
        {
            "wasm32" => 4,
            "wasm64" => 8,
            _ => throw new InvalidOperationException("The NetWasm runtime target is unsupported."),
        };
        if (target.PointerSizeBytes != expectedPointerSize ||
            target.Alignment <= 0 ||
            (target.Alignment & (target.Alignment - 1)) != 0 ||
            target.RuntimeFootprintBytes < target.NativeStackSizeBytes ||
            target.NativeStackSizeBytes != 65_536 ||
            target.DefaultInitialHeapSizeBytes < 0 ||
            target.DefaultMaximumMemorySizeBytes <= 0 ||
            target.DefaultMaximumMemorySizeBytes > target.MaximumMemorySizeBytes ||
            target.MaximumMemorySizeBytes % wasmPageSize != 0)
        {
            throw new InvalidOperationException("The NetWasm runtime target layout is invalid.");
        }

        ValidateAsset(target.RuntimeArchive);
        ValidateAsset(target.CollectorArchive);
        ValidateAsset(target.AllowedUndefinedSymbols);
        if (!string.Equals(target.RuntimeArchive.Path,
                $"{target.Target}/{target.GarbageCollector.ToLowerInvariant()}/libnetwasm-runtime.a", StringComparison.Ordinal) ||
            !string.Equals(target.CollectorArchive.Path,
                $"{target.Target}/{target.GarbageCollector.ToLowerInvariant()}/libgc.a", StringComparison.Ordinal) ||
            !string.Equals(target.AllowedUndefinedSymbols.Path,
                $"{target.Target}/allowed-undefined-symbols.txt", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The NetWasm runtime target asset path is invalid.");
        }
        ValidateSpeedAssets(target);
        if (target.SystemLibraries is null ||
            target.SystemLibraries.Names.IsDefaultOrEmpty ||
            target.SystemLibraries.Names.Any(name => string.IsNullOrWhiteSpace(name) || Path.GetFileName(name) != name) ||
            target.SystemLibraries.Names.Distinct(StringComparer.Ordinal).Count() != target.SystemLibraries.Names.Length ||
            target.SystemLibraries.Assets.IsDefaultOrEmpty ||
            target.SystemLibraries.Assets.Length != target.SystemLibraries.Names.Length)
        {
            throw new InvalidOperationException("The NetWasm runtime link closure is empty.");
        }

        for (var index = 0; index < target.SystemLibraries.Assets.Length; index++)
        {
            var asset = target.SystemLibraries.Assets[index];
            ValidateAsset(asset);
            if (!string.Equals(asset.Path,
                $"{target.Target}/system-libraries/{target.SystemLibraries.Names[index]}",
                StringComparison.Ordinal))
            {
                throw new InvalidOperationException("The NetWasm runtime system-library path is invalid.");
            }
        }
    }

    private static void ValidateSpeedAssets(RuntimePackTarget target)
    {
        var speed = target.Speed;
        if (speed is null || speed.RuntimeFootprintBytes < target.NativeStackSizeBytes)
        {
            throw new InvalidOperationException("The NetWasm runtime speed asset contract is invalid.");
        }

        ValidateAsset(speed.RuntimeArchive);
        ValidateAsset(speed.CollectorArchive);
        var prefix = $"{target.Target}/{target.GarbageCollector.ToLowerInvariant()}/speed/";
        if (!string.Equals(speed.RuntimeArchive.Path, prefix + "libnetwasm-runtime.a", StringComparison.Ordinal) ||
            !string.Equals(speed.CollectorArchive.Path, prefix + "libgc.a", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The NetWasm runtime speed asset path is invalid.");
        }
    }

    private static void ValidateAsset(RuntimePackAsset asset)
    {
        ArgumentNullException.ThrowIfNull(asset);
        if (string.IsNullOrWhiteSpace(asset.Path) ||
            Path.IsPathRooted(asset.Path) ||
            asset.Path.Replace('\\', '/').Split('/').Contains("..", StringComparer.Ordinal) ||
            asset.Sha256 is null || asset.Sha256.Length != 64 ||
            !asset.Sha256.All(Uri.IsHexDigit))
        {
            throw new InvalidOperationException("The NetWasm runtime pack contains an invalid asset descriptor.");
        }
    }
}

[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(RuntimePackManifest))]
[JsonSerializable(typeof(RuntimeLayout))]
[JsonSerializable(typeof(RuntimeNativeCacheEvidence))]
[JsonSerializable(typeof(ImmutableArray<RuntimeLinkExport>))]
internal sealed partial class RuntimePackJsonContext : JsonSerializerContext;
