using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace NetWasm.Toolchain.Manifest;

public sealed class JsonToolchainManifestReader : IToolchainManifestReader
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    public PinnedToolchainManifest Read(string manifestPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(manifestPath);

        var json = File.ReadAllText(manifestPath);
        try
        {
            var manifest = JsonSerializer.Deserialize<PinnedToolchainManifest>(json, SerializerOptions);
            return manifest ?? throw new InvalidDataException("The toolchain manifest is empty.");
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("The toolchain manifest is invalid.", exception);
        }
    }
}
