using System;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace NetWasm.Runtime.Pack.Materialization;

internal sealed class RuntimePackManifestReader : IRuntimePackManifestReader
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

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
            manifest = JsonSerializer.Deserialize<RuntimePackManifest>(json, JsonOptions);
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
        if (manifest is null || manifest.SchemaVersion != 1 || manifest.WasmPageSize != 65_536)
        {
            throw new InvalidOperationException("The NetWasm runtime pack manifest schema is unsupported.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(manifest.RuntimeAbi);
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
        if (target.LinkInputs.IsDefaultOrEmpty)
        {
            throw new InvalidOperationException("The NetWasm runtime link closure is empty.");
        }

        foreach (var asset in target.LinkInputs)
        {
            ValidateAsset(asset);
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
