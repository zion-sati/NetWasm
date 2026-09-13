using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using NetWasm.Compiler.ComponentModel.Worlds;

namespace NetWasm.Compiler.ComponentModel.Catalogs;

public sealed class WitTypeIdentityFormatter(
    IWitInterfaceSpecifierFormatter interfaces) : IWitTypeIdentityFormatter
{
    private readonly IWitInterfaceSpecifierFormatter _interfaces = interfaces ??
        throw new ArgumentNullException(nameof(interfaces));

    public string Format(WitDocument document, WitTypeReference reference)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(reference);
        if (document.Types.IsDefault || document.Interfaces.IsDefault)
        {
            throw ComponentException.Invalid(
                "WIT type identity inputs must contain explicit collections");
        }

        return FormatReference(document, reference, []);
    }

    private string FormatReference(
        WitDocument document,
        WitTypeReference reference,
        HashSet<int> path) => reference switch
        {
            WitTypeReference.Primitive primitive => FormatPrimitive(primitive.Name),
            WitTypeReference.Defined defined => FormatDefined(document, defined.Id, path),
            _ => throw new ArgumentOutOfRangeException(nameof(reference)),
        };

    private string FormatDefined(WitDocument document, int id, HashSet<int> path)
    {
        var definition = ReadDefinition(document, id);
        if (definition.Name is not null)
        {
            return FormatNamedType(document, definition);
        }
        if (!path.Add(id))
        {
            throw ComponentException.Invalid("WIT type identity contains an anonymous cycle");
        }

        try
        {
            var kind = ReadKind(definition);
            return kind.Name switch
            {
                "type" => FormatReference(document, ReadReference(kind.Value), path),
                "list" => $"list<{FormatReference(document, ReadReference(kind.Value), path)}>",
                "record" => FormatFields(document, "record", kind.Value, path),
                "tuple" => FormatTuple(document, kind.Value, path),
                "option" => $"option<{FormatReference(document, ReadReference(kind.Value), path)}>",
                "result" => FormatResult(document, kind.Value, path),
                "variant" => FormatCases(document, "variant", kind.Value, path),
                "enum" => FormatNames("enum", kind.Value, "cases"),
                "flags" => FormatNames("flags", kind.Value, "flags"),
                "handle" => FormatHandle(document, kind.Value),
                "future" or "stream" => throw ComponentException.Invalid(
                    $"user-defined WIT {kind.Name} types are not supported"),
                _ => throw ComponentException.Invalid(
                    $"unsupported WIT type '{kind.Name}'"),
            };
        }
        finally
        {
            path.Remove(id);
        }
    }

    private string FormatNamedType(WitDocument document, WitTypeDefinition definition)
    {
        if (string.IsNullOrWhiteSpace(definition.Name)
            || definition.OwnerInterface is not { } owner
            || (uint)owner >= (uint)document.Interfaces.Length)
        {
            throw ComponentException.Invalid(
                "named WIT type identity requires an owning interface");
        }
        var ownerDefinition = document.Interfaces[owner] ?? throw ComponentException.Invalid(
            "named WIT type identity references a missing interface");
        return $"{_interfaces.Format(ownerDefinition)}#{definition.Name}";
    }

    private string FormatFields(
        WitDocument document,
        string name,
        JsonElement value,
        HashSet<int> path)
    {
        var fields = ReadArrayProperty(value, "fields", "WIT record");
        return $"{name}<" + string.Join(',', fields.Select(field =>
            $"{ReadName(field, "WIT record field")}:" +
            FormatReference(document, ReadReference(
                ReadProperty(field, "type", "WIT record field")), path))) + ">";
    }

    private string FormatTuple(
        WitDocument document,
        JsonElement value,
        HashSet<int> path)
    {
        var types = ReadArrayProperty(value, "types", "WIT tuple");
        return "tuple<" + string.Join(',', types.Select(type =>
            FormatReference(document, ReadReference(type), path))) + ">";
    }

    private string FormatResult(
        WitDocument document,
        JsonElement value,
        HashSet<int> path) => "result<" +
        FormatOptionalReference(document, ReadProperty(value, "ok", "WIT result"), path) + "," +
        FormatOptionalReference(document, ReadProperty(value, "err", "WIT result"), path) + ">";

    private string FormatCases(
        WitDocument document,
        string name,
        JsonElement value,
        HashSet<int> path) => $"{name}<" + string.Join(',',
        ReadArrayProperty(value, "cases", "WIT variant").Select(@case =>
        {
            var caseName = ReadName(@case, "WIT variant case");
            var type = ReadProperty(@case, "type", "WIT variant case");
            return type.ValueKind == JsonValueKind.Null
                ? caseName
                : $"{caseName}:{FormatReference(document, ReadReference(type), path)}";
        })) + ">";

    private static string FormatNames(string name, JsonElement value, string property) =>
        $"{name}<" + string.Join(',', ReadArrayProperty(value, property, $"WIT {name}")
            .Select(item => ReadName(item, $"WIT {name} member"))) + ">";

    private string FormatHandle(WitDocument document, JsonElement value)
    {
        var property = ReadSingleProperty(value, "WIT handle");
        if (property.Name is not ("own" or "borrow")
            || property.Value.ValueKind != JsonValueKind.Number)
        {
            throw ComponentException.Invalid("WIT handle identity is invalid");
        }
        return $"{property.Name}<{ResolveResource(document, property.Value.GetInt32(), [])}>";
    }

    private string ResolveResource(WitDocument document, int id, HashSet<int> path)
    {
        var definition = ReadDefinition(document, id);
        if (!path.Add(id))
        {
            throw ComponentException.Invalid("WIT resource identity contains an alias cycle");
        }

        try
        {
            if (definition.Kind.ValueKind == JsonValueKind.String)
            {
                if (definition.Kind.GetString() != "resource" || definition.Name is null)
                {
                    throw ComponentException.Invalid(
                        "WIT handle does not reference a named resource type");
                }
                return FormatNamedType(document, definition);
            }

            var kind = ReadKind(definition);
            if (kind.Name != "type" || kind.Value.ValueKind != JsonValueKind.Number)
            {
                throw ComponentException.Invalid(
                    "WIT handle does not reference a resource type");
            }
            return ResolveResource(document, kind.Value.GetInt32(), path);
        }
        finally
        {
            path.Remove(id);
        }
    }

    private string FormatOptionalReference(
        WitDocument document,
        JsonElement value,
        HashSet<int> path) => value.ValueKind == JsonValueKind.Null
        ? "unit"
        : FormatReference(document, ReadReference(value), path);

    private static string FormatPrimitive(string name) => name switch
    {
        "bool" or "s8" or "u8" or "s16" or "u16" or "s32" or "u32" or
        "s64" or "u64" or "f32" or "f64" or "char" or "string" => name,
        _ => throw ComponentException.Invalid($"unsupported WIT primitive '{name}'"),
    };

    private static WitTypeDefinition ReadDefinition(WitDocument document, int id)
    {
        if ((uint)id >= (uint)document.Types.Length)
        {
            throw ComponentException.Invalid("WIT type identity references an unknown type");
        }
        return document.Types[id] ?? throw ComponentException.Invalid(
            "WIT type identity references a missing type definition");
    }

    private static JsonProperty ReadKind(WitTypeDefinition definition)
    {
        if (definition.Kind.ValueKind != JsonValueKind.Object)
        {
            throw ComponentException.Invalid("anonymous WIT type identity is invalid");
        }
        return ReadSingleProperty(definition.Kind, "WIT type");
    }

    private static JsonProperty ReadSingleProperty(JsonElement value, string label)
    {
        if (value.ValueKind != JsonValueKind.Object)
        {
            throw ComponentException.Invalid($"{label} identity is invalid");
        }
        var properties = value.EnumerateObject().ToArray();
        if (properties.Length != 1)
        {
            throw ComponentException.Invalid($"{label} identity must declare exactly one kind");
        }
        return properties[0];
    }

    private static JsonElement.ArrayEnumerator ReadArrayProperty(
        JsonElement value,
        string property,
        string label)
    {
        var member = ReadProperty(value, property, label);
        if (member.ValueKind != JsonValueKind.Array)
        {
            throw ComponentException.Invalid($"{label} identity is invalid");
        }
        return member.EnumerateArray();
    }

    private static JsonElement ReadProperty(
        JsonElement value,
        string property,
        string label)
    {
        if (value.ValueKind != JsonValueKind.Object
            || !value.TryGetProperty(property, out var member))
        {
            throw ComponentException.Invalid($"{label} identity is invalid");
        }
        return member;
    }

    private static string ReadName(JsonElement value, string label)
    {
        var name = ReadProperty(value, "name", label);
        if (name.ValueKind != JsonValueKind.String
            || string.IsNullOrWhiteSpace(name.GetString()))
        {
            throw ComponentException.Invalid($"{label} identity is invalid");
        }
        return name.GetString()!;
    }

    private static WitTypeReference ReadReference(JsonElement value) =>
        value.ValueKind == JsonValueKind.String
            ? new WitTypeReference.Primitive(value.GetString()!)
            : value.ValueKind == JsonValueKind.Number
                ? new WitTypeReference.Defined(value.GetInt32())
                : throw ComponentException.Invalid("WIT type reference is invalid");

}
