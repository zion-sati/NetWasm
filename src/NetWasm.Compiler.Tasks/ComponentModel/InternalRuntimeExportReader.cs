using System.Collections.Generic;
using System.Collections.Immutable;
using System.Text.Json;
using NetWasm.Compiler.ComponentModel;

namespace NetWasm.Compiler.Tasks.ComponentModel;

internal sealed class InternalRuntimeExportReader : IInternalRuntimeExportReader
{
    public ImmutableArray<WasmInternalExport> Read(string metadata)
    {
        if (string.IsNullOrWhiteSpace(metadata)) return [];
        try
        {
            using var document = JsonDocument.Parse(metadata);
            if (document.RootElement.ValueKind != JsonValueKind.Array) throw Invalid();
            var exports = ImmutableArray.CreateBuilder<WasmInternalExport>();
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var item in document.RootElement.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object) throw Invalid();
                var fields = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
                foreach (var property in item.EnumerateObject())
                    if (!fields.TryAdd(property.Name, property.Value)) throw Invalid();
                if (fields.Count != 2 || !fields.TryGetValue(nameof(WasmInternalExport.Name), out var name) ||
                    !fields.TryGetValue(nameof(WasmInternalExport.Kind), out var kind) ||
                    name.ValueKind != JsonValueKind.String || kind.ValueKind != JsonValueKind.Number ||
                    !kind.TryGetByte(out var value) || value is not (0 or 3)) throw Invalid();
                var text = name.GetString()!;
                if (string.IsNullOrWhiteSpace(text) || text.IndexOfAny(['\0', '\r', '\n']) >= 0 || !names.Add(text)) throw Invalid();
                exports.Add(new(text, value));
            }
            return exports.ToImmutable();
        }
        catch (JsonException error) { throw new InvalidOperationException("The internal runtime export metadata is malformed.", error); }
    }

    private static InvalidOperationException Invalid() => new("The internal runtime export metadata is invalid.");
}
