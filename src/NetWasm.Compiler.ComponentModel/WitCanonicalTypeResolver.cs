using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.ComponentModel;

public interface IWitCanonicalTypeResolver
{
    CanonicalAbiType Resolve(WitDocument document, WitTypeReference reference);
}

public sealed class WitCanonicalTypeResolver : IWitCanonicalTypeResolver
{
    public CanonicalAbiType Resolve(
        WitDocument document,
        WitTypeReference reference)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(reference);
        return reference switch
        {
            WitTypeReference.Primitive primitive => Primitive(primitive.Name),
            WitTypeReference.Defined defined => Defined(
                document,
                document.Types[defined.Id]),
            _ => throw new ArgumentOutOfRangeException(nameof(reference)),
        };
    }

    private CanonicalAbiType Defined(
        WitDocument document,
        WitTypeDefinition definition)
    {
        if (definition.Kind.ValueKind == JsonValueKind.String)
        {
            if (definition.Kind.GetString() != "resource")
            {
                throw ComponentException.Invalid(
                    $"unsupported WIT type '{definition.Kind.GetString()}'");
            }
            return Resource(CanonicalAbiTypeKind.OwnedResource, definition.Id);
        }

        var kind = definition.Kind.EnumerateObject().Single();
        return kind.Name switch
        {
            "type" => new(CanonicalAbiTypeKind.Alias, Placeholder())
            {
                ElementType = Resolve(document, Reference(kind.Value)),
            },
            "list" => new(CanonicalAbiTypeKind.List, Placeholder())
            {
                ElementType = Resolve(document, Reference(kind.Value)),
            },
            "record" => new(CanonicalAbiTypeKind.Record, Placeholder())
            {
                Fields = [.. kind.Value.GetProperty("fields").EnumerateArray()
                    .Select(field => new CanonicalAbiField(
                        field.GetProperty("name").GetString()!,
                        Resolve(document, Reference(field.GetProperty("type")))))],
            },
            "tuple" => new(CanonicalAbiTypeKind.Tuple, Placeholder())
            {
                Fields = [.. kind.Value.GetProperty("types").EnumerateArray()
                    .Select((type, index) => new CanonicalAbiField(
                        $"item{index}",
                        Resolve(document, Reference(type))))],
            },
            "option" => new(CanonicalAbiTypeKind.Option, Placeholder())
            {
                ElementType = Resolve(document, Reference(kind.Value)),
            },
            "result" => new(CanonicalAbiTypeKind.Result, Placeholder())
            {
                SuccessType = ResultArm(document, kind.Value, "ok"),
                ErrorType = ResultArm(document, kind.Value, "err"),
            },
            "variant" => new(CanonicalAbiTypeKind.Variant, Placeholder())
            {
                Cases = [.. kind.Value.GetProperty("cases").EnumerateArray()
                    .Select(item => new CanonicalAbiCase(
                        item.GetProperty("name").GetString()!,
                        item.GetProperty("type").ValueKind == JsonValueKind.Null
                            ? null
                            : Resolve(
                                document,
                                Reference(item.GetProperty("type")))))],
            },
            "enum" => new(CanonicalAbiTypeKind.Enum, Placeholder())
            {
                Cases = [.. kind.Value.GetProperty("cases").EnumerateArray()
                    .Select(item => new CanonicalAbiCase(
                        item.GetProperty("name").GetString()!,
                        null))],
            },
            "flags" => new(CanonicalAbiTypeKind.Flags, Placeholder())
            {
                FlagsCount = kind.Value.GetProperty("flags").GetArrayLength(),
            },
            "handle" => Handle(document, kind.Value),
            "future" or "stream" => throw ComponentException.Invalid(
                $"user-defined WIT {kind.Name} types are not supported"),
            _ => throw ComponentException.Invalid(
                $"unsupported WIT type '{kind.Name}'"),
        };
    }

    private CanonicalAbiType ResultArm(
        WitDocument document,
        JsonElement result,
        string name)
    {
        var value = result.GetProperty(name);
        return value.ValueKind == JsonValueKind.Null
            ? new(CanonicalAbiTypeKind.Unit, Placeholder())
            : Resolve(document, Reference(value));
    }

    private static CanonicalAbiType Handle(
        WitDocument document,
        JsonElement value)
    {
        var property = value.EnumerateObject().Single();
        var resourceId = ResourceTypeId(
            document,
            property.Value.GetInt32(),
            []);
        return Resource(
            property.Name == "own"
                ? CanonicalAbiTypeKind.OwnedResource
                : CanonicalAbiTypeKind.BorrowedResource,
            resourceId);
    }

    private static int ResourceTypeId(
        WitDocument document,
        int typeId,
        HashSet<int> visited)
    {
        if ((uint)typeId >= (uint)document.Types.Length || !visited.Add(typeId))
        {
            throw ComponentException.Invalid(
                "WIT handle does not reference a resource type");
        }

        var kind = document.Types[typeId].Kind;
        if (kind.ValueKind == JsonValueKind.String)
        {
            return kind.GetString() == "resource"
                ? typeId
                : throw ComponentException.Invalid(
                    "WIT handle does not reference a resource type");
        }

        var property = kind.EnumerateObject().Single();
        return property.Name == "type" &&
            property.Value.ValueKind == JsonValueKind.Number
                ? ResourceTypeId(document, property.Value.GetInt32(), visited)
                : throw ComponentException.Invalid(
                    "WIT handle does not reference a resource type");
    }

    private static CanonicalAbiType Resource(
        CanonicalAbiTypeKind kind,
        int resourceId) => new(kind, Placeholder())
        {
            ResourceTypeId = resourceId,
        };

    private static CanonicalAbiType Primitive(string name) => new(
        name switch
        {
            "bool" => CanonicalAbiTypeKind.Bool,
            "s8" => CanonicalAbiTypeKind.S8,
            "u8" => CanonicalAbiTypeKind.U8,
            "s16" => CanonicalAbiTypeKind.S16,
            "u16" => CanonicalAbiTypeKind.U16,
            "s32" => CanonicalAbiTypeKind.S32,
            "u32" => CanonicalAbiTypeKind.U32,
            "s64" => CanonicalAbiTypeKind.S64,
            "u64" => CanonicalAbiTypeKind.U64,
            "f32" => CanonicalAbiTypeKind.F32,
            "f64" => CanonicalAbiTypeKind.F64,
            "char" => CanonicalAbiTypeKind.Character,
            "string" => CanonicalAbiTypeKind.Text,
            _ => throw ComponentException.Invalid(
                $"unsupported WIT primitive '{name}'"),
        },
        Placeholder());

    private static WitTypeReference Reference(JsonElement value) =>
        value.ValueKind == JsonValueKind.String
            ? new WitTypeReference.Primitive(value.GetString()!)
            : new WitTypeReference.Defined(value.GetInt32());

    private static CliTypeIdentity Placeholder() =>
        CliTypeIdentity.FromStackKind(CliValueKind.Unknown);
}
