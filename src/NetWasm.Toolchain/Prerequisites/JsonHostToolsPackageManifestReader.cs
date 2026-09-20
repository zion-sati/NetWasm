using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Text.Json;

namespace NetWasm.Toolchain.Prerequisites;

public sealed class JsonHostToolsPackageManifestReader : IHostToolsPackageManifestReader
{
    public HostToolsPackageManifest Read(ReadOnlyMemory<byte> json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.GetProperty("schemaVersion").GetInt32() != 1)
            {
                throw new InvalidDataException("Unsupported host-tools manifest schema.");
            }

            var upstream = root.GetProperty("upstream");
            var files = ImmutableArray.CreateBuilder<HostToolsPackageFile>();
            foreach (var file in root.GetProperty("files").EnumerateArray())
            {
                files.Add(new(
                    file.GetProperty("path").GetString() ?? string.Empty,
                    file.GetProperty("size").GetInt64(),
                    file.GetProperty("sha256").GetString() ?? string.Empty));
            }

            var roles = ImmutableDictionary.CreateBuilder<string, string>(StringComparer.Ordinal);
            foreach (var role in root.GetProperty("roles").EnumerateObject())
            {
                roles.Add(role.Name, role.Value.GetString() ?? string.Empty);
            }

            return new(
                root.GetProperty("packageId").GetString() ?? string.Empty,
                root.GetProperty("packageVersion").GetString() ?? string.Empty,
                root.GetProperty("hostRid").GetString() ?? string.Empty,
                upstream.GetProperty("node").GetString() ?? string.Empty,
                upstream.GetProperty("llvmLld").GetProperty("version").GetString() ?? string.Empty,
                upstream.GetProperty("binaryen").GetString() ?? string.Empty,
                roles.ToImmutable(),
                files.ToImmutable());
        }
        catch (Exception exception) when (exception is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            throw new InvalidDataException("The host-tools package manifest is malformed.", exception);
        }
    }
}
