using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text.Json;

namespace NetWasm.Compiler.ComponentModel;

public interface IWitDocumentReader
{
    WitDocument Read(string witPath);

    WitDocument Read(string witPath, string? world) => Read(witPath);
}

public interface IWitDocumentJsonReader
{
    WitDocument Read(string normalizedJson);
}

public sealed class WitDocumentReader : IWitDocumentReader
{
    private readonly IWasmTools _tools;
    private readonly IWitDocumentJsonReader _json;
    private readonly IWitCoreBindingResolver _coreBindings;

    public WitDocumentReader(IWasmTools tools)
        : this(tools, new WitDocumentJsonReader(),
            new WitCoreBindingResolver(tools, new Worlds.WitWorldSpecifierFormatter()))
    {
    }

    public WitDocumentReader(IWasmTools tools, IWitDocumentJsonReader json)
        : this(tools, json,
            new WitCoreBindingResolver(tools, new Worlds.WitWorldSpecifierFormatter()))
    {
    }

    public WitDocumentReader(
        IWasmTools tools,
        IWitDocumentJsonReader json,
        IWitCoreBindingResolver coreBindings)
    {
        _tools = tools ?? throw new ArgumentNullException(nameof(tools));
        _json = json ?? throw new ArgumentNullException(nameof(json));
        _coreBindings = coreBindings ?? throw new ArgumentNullException(nameof(coreBindings));
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

    public WitDocument Read(string witPath, string? world)
    {
        var document = Read(witPath);
        return _coreBindings.Resolve(witPath, document, document.SelectWorld(world));
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
        // A package's interface is a world item by its key, whatever world names it.
        var keys = root.GetProperty("interfaces").EnumerateArray()
            .Select(item => item.GetProperty("name").ValueKind == JsonValueKind.String
                ? WitInterface.FormatKey(packages[item.GetProperty("package").GetInt32()].Name,
                    item.GetProperty("name").GetString()!)
                : null)
            .ToImmutableArray();
        var worlds = root.GetProperty("worlds").EnumerateArray()
            .Select((item, id) => ReadWorld(item, id, packages, keys))
            .ToImmutableArray();
        // An interface a world declares inline has no name of its own: the
        // world's name for it is its name and its key.
        var inline = worlds
            .SelectMany(static world => world.Imports.Concat(world.Exports))
            .Where(static item => item.InterfaceId is not null)
            .GroupBy(static item => item.InterfaceId!.Value)
            .ToDictionary(static group => group.Key, static group => group.First().Name);
        var interfaces = root.GetProperty("interfaces").EnumerateArray()
            .Select((item, id) => ReadInterface(item, id, packages, inline))
            .ToImmutableArray();
        var types = root.GetProperty("types").EnumerateArray()
            .Select((item, id) => new WitTypeDefinition(
                id,
                item.GetProperty("name").ValueKind == JsonValueKind.Null
                    ? null
                    : item.GetProperty("name").GetString(),
                item.GetProperty("kind").Clone(),
                ReadOwner(item.GetProperty("owner"), "interface"))
            {
                OwnerWorld = ReadOwner(item.GetProperty("owner"), "world"),
            })
            .ToImmutableArray();
        return new WitDocument(packages, interfaces, worlds, types, normalizedJson);
    }

    private static WitInterface ReadInterface(
        JsonElement item,
        int id,
        ImmutableArray<WitPackage> packages,
        Dictionary<int, string> worldNames)
    {
        var packageId = item.GetProperty("package").GetInt32();
        var name = item.GetProperty("name");
        var definition = new WitInterface(
            id,
            name.ValueKind == JsonValueKind.String
                ? name.GetString()!
                : worldNames.TryGetValue(id, out var worldName)
                    ? worldName
                    : throw ComponentException.Invalid(
                        $"WIT interface {id} has no name and no world places it"),
            packages[packageId].Name,
            ReadNameMap(item.GetProperty("types")),
            item.GetProperty("functions").EnumerateObject()
                .Select(function => ReadFunction(function.Value))
                .ToImmutableArray());
        return name.ValueKind == JsonValueKind.String ? definition : definition with { Key = definition.Name };
    }

    private static WitWorld ReadWorld(
        JsonElement item,
        int id,
        ImmutableArray<WitPackage> packages,
        ImmutableArray<string?> keys)
    {
        var packageId = item.GetProperty("package").GetInt32();
        var imports = item.GetProperty("imports");
        return new WitWorld(
            id,
            item.GetProperty("name").GetString()!,
            packages[packageId].Name,
            ReadWorldItems(imports, keys),
            ReadWorldItems(item.GetProperty("exports"), keys))
        {
            // A type the world declares is listed among its imports.
            Types = imports.EnumerateObject()
                .Where(static entry => entry.Value.TryGetProperty("type", out _))
                .ToImmutableDictionary(
                    static entry => entry.Name,
                    static entry => entry.Value.GetProperty("type").GetInt32(),
                    StringComparer.Ordinal),
        };
    }

    // Each item by its world key; an interface the world names from a package is
    // keyed `interface-<id>` in the JSON, and gets its key from the interface.
    private static ImmutableArray<WitWorldItem> ReadWorldItems(JsonElement items, ImmutableArray<string?> keys) =>
        items.EnumerateObject().Where(static item => !item.Value.TryGetProperty("type", out _)).Select(item =>
        {
            if (item.Value.TryGetProperty("interface", out var interfaceValue))
            {
                var interfaceId = interfaceValue.GetProperty("id").GetInt32();
                return new WitWorldItem(
                    item.Name.StartsWith("interface-", StringComparison.Ordinal) && keys[interfaceId] is { } key
                        ? key
                        : item.Name,
                    interfaceId,
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

    // The interface or world, by `kind`, that owns a type.
    private static int? ReadOwner(JsonElement owner, string kind)
    {
        if (owner.ValueKind == JsonValueKind.Null)
        {
            return null;
        }
        return owner.TryGetProperty(kind, out var ownerId)
            ? ownerId.GetInt32()
            : null;
    }

}
