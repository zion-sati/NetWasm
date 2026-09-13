using System;
using System.IO;
using System.Text.Json;

namespace NetWasm.Runtime.Pack.Materialization;

internal sealed class RuntimeLayoutReader : IRuntimeLayoutReader
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public RuntimeLayout Read(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (!File.Exists(path))
        {
            throw new InvalidOperationException("The NetWasm runtime layout evidence is missing.");
        }

        RuntimeLayout? layout;
        try
        {
            layout = JsonSerializer.Deserialize<RuntimeLayout>(File.ReadAllText(path), JsonOptions);
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException("The NetWasm runtime layout evidence is malformed.", exception);
        }

        if (layout is null ||
            layout.SchemaVersion != 2 ||
            string.IsNullOrWhiteSpace(layout.Target) ||
            layout.ApplicationStaticDataEnd < 0)
        {
            throw new InvalidOperationException("The NetWasm runtime layout evidence is invalid.");
        }

        return layout;
    }
}
