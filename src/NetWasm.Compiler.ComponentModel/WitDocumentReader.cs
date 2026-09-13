using System;
using System.Collections.Immutable;
using System.Linq;
using System.Text.Json;

namespace NetWasm.Compiler.ComponentModel;

public interface IWitDocumentReader
{
    WitDocument Read(string witPath);
}

public interface IWitDocumentJsonReader
{
    WitDocument Read(string normalizedJson);
}

public sealed class WitDocumentReader : IWitDocumentReader
{
    private readonly IWasmTools _tools;
    private readonly IWitDocumentJsonReader _json;

    public WitDocumentReader(IWasmTools tools)
        : this(tools, new WitDocumentJsonReader())
    {
    }

    public WitDocumentReader(IWasmTools tools, IWitDocumentJsonReader json)
    {
        _tools = tools ?? throw new ArgumentNullException(nameof(tools));
        _json = json ?? throw new ArgumentNullException(nameof(json));
    }

    public WitDocument Read(string witPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(witPath);
        var result = _tools.Run("component", "wit", witPath, "--json", "--no-docs");
        if (result.ExitCode != 0)
        {
            throw ComponentException.Invalid(
                $"invalid WIT input: {NormalizeError(result.StandardError)}");
        }

        return _json.Read(result.StandardOutput);
    }

    private static string NormalizeError(string error)
    {
        var normalized = error.Replace('\r', ' ').Replace('\n', ' ').Trim();
        return normalized.Length == 0 ? "wasm-tools reported an unknown error" : normalized;
    }
}

public sealed class WitDocumentJsonReader : IWitDocumentJsonReader
{
    public WitDocument Read(string normalizedJson)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(normalizedJson);
        try
        {
            using var json = JsonDocument.Parse(normalizedJson);
            return Parse(json.RootElement, normalizedJson);
        }
        catch (JsonException exception)
        {
            throw ComponentException.Tool(
                $"wasm-tools returned invalid WIT JSON: {exception.Message}");
        }
    }

    private static WitDocument Parse(JsonElement root, string normalizedJson)
    {
        var packages = root.GetProperty("packages").EnumerateArray()
            .Select((package, id) => new WitPackage(
                id,
                package.GetProperty("name").GetString()!,
                ReadNameMap(package.GetProperty("interfaces")),
                ReadNameMap(package.GetProperty("worlds"))))
            .ToImmutableArray();
        var interfaces = root.GetProperty("interfaces").EnumerateArray()
            .Select((item, id) => ReadInterface(item, id, packages))
            .ToImmutableArray();
        var worlds = root.GetProperty("worlds").EnumerateArray()
            .Select((item, id) => ReadWorld(item, id, packages))
            .ToImmutableArray();
        var types = root.GetProperty("types").EnumerateArray()
            .Select((item, id) => new WitTypeDefinition(
                id,
                item.GetProperty("name").ValueKind == JsonValueKind.Null
                    ? null
                    : item.GetProperty("name").GetString(),
                item.GetProperty("kind").Clone(),
                ReadOwner(item.GetProperty("owner"))))
            .ToImmutableArray();
        return new WitDocument(packages, interfaces, worlds, types, normalizedJson);
    }

    private static WitInterface ReadInterface(
        JsonElement item,
        int id,
        ImmutableArray<WitPackage> packages)
    {
        var packageId = item.GetProperty("package").GetInt32();
        return new WitInterface(
            id,
            item.GetProperty("name").GetString()!,
            packages[packageId].Name,
            ReadNameMap(item.GetProperty("types")),
            item.GetProperty("functions").EnumerateObject()
                .Select(function => ReadFunction(function.Value))
                .ToImmutableArray());
    }

    private static WitWorld ReadWorld(
        JsonElement item,
        int id,
        ImmutableArray<WitPackage> packages)
    {
        var packageId = item.GetProperty("package").GetInt32();
        return new WitWorld(
            id,
            item.GetProperty("name").GetString()!,
            packages[packageId].Name,
            ReadWorldItems(item.GetProperty("imports")),
            ReadWorldItems(item.GetProperty("exports")));
    }

    private static ImmutableArray<WitWorldItem> ReadWorldItems(JsonElement items) =>
        items.EnumerateObject().Select(item =>
        {
            if (item.Value.TryGetProperty("interface", out var interfaceValue))
            {
                return new WitWorldItem(
                    item.Name,
                    interfaceValue.GetProperty("id").GetInt32(),
                    null);
            }
            if (item.Value.TryGetProperty("function", out var functionValue))
            {
                return new WitWorldItem(item.Name, null, ReadFunction(functionValue));
            }
            throw ComponentException.Invalid(
                $"unsupported WIT world item '{item.Name}'");
        }).ToImmutableArray();

    private static WitFunction ReadFunction(JsonElement function)
    {
        var kindElement = function.GetProperty("kind");
        var kind = kindElement.ValueKind == JsonValueKind.String
            ? new WitFunctionKind(kindElement.GetString()!)
            : new WitFunctionKind(
                kindElement.EnumerateObject().Single().Name,
                kindElement.EnumerateObject().Single().Value.GetInt32());
        var hasResult = function.TryGetProperty("result", out var result);
        return new WitFunction(
            function.GetProperty("name").GetString()!,
            function.GetProperty("params").EnumerateArray()
                .Select(parameter => new WitParameter(
                    parameter.GetProperty("name").GetString()!,
                    ReadTypeReference(parameter.GetProperty("type"))))
                .ToImmutableArray(),
            !hasResult || result.ValueKind == JsonValueKind.Null
                ? null
                : ReadTypeReference(result),
            kind);
    }

    private static WitTypeReference ReadTypeReference(JsonElement type) =>
        type.ValueKind == JsonValueKind.String
            ? new WitTypeReference.Primitive(type.GetString()!)
            : new WitTypeReference.Defined(type.GetInt32());

    private static ImmutableDictionary<string, int> ReadNameMap(JsonElement map) =>
        map.EnumerateObject().ToImmutableDictionary(
            pair => pair.Name,
            pair => pair.Value.GetInt32(),
            StringComparer.Ordinal);

    private static int? ReadOwner(JsonElement owner)
    {
        if (owner.ValueKind == JsonValueKind.Null)
        {
            return null;
        }
        return owner.TryGetProperty("interface", out var interfaceId)
            ? interfaceId.GetInt32()
            : null;
    }

}
