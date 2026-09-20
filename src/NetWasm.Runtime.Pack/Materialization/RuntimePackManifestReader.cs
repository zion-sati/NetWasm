using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace NetWasm.Runtime.Pack.Materialization;

internal sealed class RuntimePackManifestReader : IRuntimePackManifestReader
{
    public RuntimePackManifest Read(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (!File.Exists(path))
        {
            throw new InvalidOperationException("The NetWasm runtime pack manifest is missing.");
        }

        return ReadJson(File.ReadAllText(path));
    }

    public static RuntimePackManifest ReadJson(string json)
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
        return manifest!;
    }

    private static void Validate(RuntimePackManifest? manifest)
    {
        if (manifest is null || manifest.SchemaVersion != 3 || manifest.WasmPageSize != 65_536)
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

        if (manifest.Targets.Length != 2 ||
            manifest.Targets.Select(static target => target.Target).Distinct(StringComparer.Ordinal).Count() != 2)
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

    private static void ValidateAsset(RuntimePackAsset asset)
    {
        ArgumentNullException.ThrowIfNull(asset);
        if (string.IsNullOrWhiteSpace(asset.Path) ||
            Path.IsPathRooted(asset.Path) ||
            asset.Path.Replace('\\', '/').Split('/').Contains("..", StringComparer.Ordinal) ||
            asset.Sha256.Length != 64 ||
            !asset.Sha256.All(Uri.IsHexDigit))
        {
            throw new InvalidOperationException("The NetWasm runtime pack contains an invalid asset descriptor.");
        }
    }
}

[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(RuntimePackManifest))]
internal sealed partial class RuntimePackJsonContext : JsonSerializerContext;
