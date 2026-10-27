using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using NetWasm.Toolchain;

namespace NetWasm.Toolchain.Manifest;

public sealed class JsonToolchainManifestReader : IToolchainManifestReader
{
    private static readonly ToolchainJsonContext Json = new(new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    });

    public PinnedToolchainManifest Read(string manifestPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(manifestPath);

        var json = File.ReadAllText(manifestPath);
        try
        {
            var manifest = JsonSerializer.Deserialize(json, Json.PinnedToolchainManifest);
            return manifest ?? throw new InvalidDataException("The toolchain manifest is empty.");
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("The toolchain manifest is invalid.", exception);
        }
    }
}
