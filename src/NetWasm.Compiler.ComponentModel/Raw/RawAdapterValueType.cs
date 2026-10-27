using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text.Json;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.ComponentModel.Raw;

/// <summary>
/// A canonical value type as a raw adapter binding describes it: the kind tree
/// the adapter writer serializes, with record field, flag and case names already
/// in their JavaScript form.
/// </summary>
internal sealed class RawAdapterValueType
{
    private static readonly FrozenDictionary<string, CanonicalAbiTypeKind> Kinds =
        new Dictionary<string, CanonicalAbiTypeKind>(StringComparer.Ordinal)
        {
            ["unit"] = CanonicalAbiTypeKind.Unit,
            ["bool"] = CanonicalAbiTypeKind.Bool,
            ["s8"] = CanonicalAbiTypeKind.S8,
            ["u8"] = CanonicalAbiTypeKind.U8,
            ["s16"] = CanonicalAbiTypeKind.S16,
            ["u16"] = CanonicalAbiTypeKind.U16,
            ["s32"] = CanonicalAbiTypeKind.S32,
            ["u32"] = CanonicalAbiTypeKind.U32,
            ["s64"] = CanonicalAbiTypeKind.S64,
            ["u64"] = CanonicalAbiTypeKind.U64,
            ["f32"] = CanonicalAbiTypeKind.F32,
            ["f64"] = CanonicalAbiTypeKind.F64,
            ["character"] = CanonicalAbiTypeKind.Character,
            ["text"] = CanonicalAbiTypeKind.Text,
            ["alias"] = CanonicalAbiTypeKind.Alias,
            ["list"] = CanonicalAbiTypeKind.List,
            ["record"] = CanonicalAbiTypeKind.Record,
            ["tuple"] = CanonicalAbiTypeKind.Tuple,
            ["option"] = CanonicalAbiTypeKind.Option,
            ["result"] = CanonicalAbiTypeKind.Result,
            ["variant"] = CanonicalAbiTypeKind.Variant,
            ["enum"] = CanonicalAbiTypeKind.Enum,
            ["flags"] = CanonicalAbiTypeKind.Flags,
            ["owned-resource"] = CanonicalAbiTypeKind.OwnedResource,
            ["borrowed-resource"] = CanonicalAbiTypeKind.BorrowedResource,
        }.ToFrozenDictionary(StringComparer.Ordinal);

    public required CanonicalAbiTypeKind Kind { get; init; }

    /// <summary>The aliased, listed or optional type.</summary>
    public RawAdapterValueType? Element { get; init; }

    public ImmutableArray<RawAdapterValueField> Fields { get; init; } = [];

    public ImmutableArray<RawAdapterValueCase> Cases { get; init; } = [];

    public RawAdapterValueType? Ok { get; init; }

    public RawAdapterValueType? Error { get; init; }

    public ImmutableArray<string> Flags { get; init; } = [];

    public int Count { get; init; }

    public int ResourceType { get; init; } = -1;

    /// <summary>The kind behind any aliases.</summary>
    public RawAdapterValueType Underlying => Kind == CanonicalAbiTypeKind.Alias ? Element!.Underlying : this;

    /// <summary>The cases of an option, result or variant, each with its payload type or null.</summary>
    public ImmutableArray<RawAdapterValueType?> SelectionTypes => Kind switch
    {
        CanonicalAbiTypeKind.Option => [null, Element],
        CanonicalAbiTypeKind.Result => [Ok, Error],
        _ => [.. Cases.Select(item => item.Type)],
    };

    /// <summary>Whether lowering a value of this type allocates guest memory.</summary>
    public bool Allocates => Kind switch
    {
        CanonicalAbiTypeKind.Text or CanonicalAbiTypeKind.List => true,
        CanonicalAbiTypeKind.Alias or CanonicalAbiTypeKind.Option => Element!.Allocates,
        CanonicalAbiTypeKind.Record or CanonicalAbiTypeKind.Tuple => Fields.Any(member => member.Type.Allocates),
        CanonicalAbiTypeKind.Result => Ok?.Allocates == true || Error?.Allocates == true,
        CanonicalAbiTypeKind.Variant => Cases.Any(item => item.Type?.Allocates == true),
        _ => false,
    };

    /// <summary>A structural identity for sharing generated code between equal types.</summary>
    public string Key => Kind switch
    {
        CanonicalAbiTypeKind.Alias or CanonicalAbiTypeKind.List or CanonicalAbiTypeKind.Option =>
            $"{Kind}({Element!.Key})",
        CanonicalAbiTypeKind.Record =>
            $"Record({string.Join(",", Fields.Select(member => $"{member.JavaScriptName}:{member.Type.Key}"))})",
        CanonicalAbiTypeKind.Tuple => $"Tuple({string.Join(",", Fields.Select(member => member.Type.Key))})",
        CanonicalAbiTypeKind.Result => $"Result({Ok?.Key ?? "_"},{Error?.Key ?? "_"})",
        CanonicalAbiTypeKind.Variant =>
            $"Variant({string.Join(",", Cases.Select(item => $"{item.Name}:{item.Type?.Key ?? "_"}"))})",
        CanonicalAbiTypeKind.Enum => $"Enum({string.Join(",", Cases.Select(item => item.Name))})",
        CanonicalAbiTypeKind.Flags => $"Flags({Count}:{string.Join(",", Flags)})",
        CanonicalAbiTypeKind.OwnedResource or CanonicalAbiTypeKind.BorrowedResource => $"{Kind}({ResourceType})",
        _ => Kind.ToString(),
    };

    public static RawAdapterValueType Read(JsonElement value)
    {
        var name = value.GetProperty("kind").GetString()!;
        if (!Kinds.TryGetValue(name, out var kind))
        {
            throw new InvalidOperationException($"Raw adapter type '{name}' is unsupported.");
        }
        return kind switch
        {
            CanonicalAbiTypeKind.Alias or CanonicalAbiTypeKind.List or CanonicalAbiTypeKind.Option =>
                new() { Kind = kind, Element = Read(value.GetProperty("element")) },
            CanonicalAbiTypeKind.Record or CanonicalAbiTypeKind.Tuple => new()
            {
                Kind = kind,
                Fields = [.. value.GetProperty("fields").EnumerateArray().Select(field => new RawAdapterValueField(
                    field.TryGetProperty("javascriptName", out var fieldName) ? fieldName.GetString() : null,
                    Read(field.GetProperty("type"))))],
            },
            CanonicalAbiTypeKind.Result => new()
            {
                Kind = kind,
                Ok = ReadOptional(value.GetProperty("ok")),
                Error = ReadOptional(value.GetProperty("error")),
            },
            CanonicalAbiTypeKind.Variant or CanonicalAbiTypeKind.Enum => new()
            {
                Kind = kind,
                Cases = [.. value.GetProperty("cases").EnumerateArray().Select(item => new RawAdapterValueCase(
                    item.GetProperty("name").GetString()!,
                    ReadOptional(item.GetProperty("type"))))],
            },
            CanonicalAbiTypeKind.Flags => new()
            {
                Kind = kind,
                Count = value.GetProperty("count").GetInt32(),
                Flags = [.. value.GetProperty("flags").EnumerateArray()
                    .Select(flag => flag.GetProperty("javascriptName").GetString()!)],
            },
            CanonicalAbiTypeKind.OwnedResource or CanonicalAbiTypeKind.BorrowedResource => new()
            {
                Kind = kind,
                ResourceType = value.GetProperty("resourceType").GetInt32(),
            },
            _ => new() { Kind = kind },
        };
    }

    public static RawAdapterValueType? ReadOptional(JsonElement value) =>
        value.ValueKind == JsonValueKind.Null ? null : Read(value);

    /// <summary>A tuple of the given types, as a function's parameters are passed.</summary>
    public static RawAdapterValueType Tuple(ImmutableArray<RawAdapterValueType> types) => new()
    {
        Kind = CanonicalAbiTypeKind.Tuple,
        Fields = [.. types.Select(type => new RawAdapterValueField(null, type))],
    };
}

internal sealed record RawAdapterValueField(string? JavaScriptName, RawAdapterValueType Type);

internal sealed record RawAdapterValueCase(string Name, RawAdapterValueType? Type);

/// <summary>A canonical value's size, alignment and, for records and tuples, field offsets.</summary>
internal sealed record RawAdapterValueLayout(
    int Size,
    int Alignment,
    ImmutableArray<int> FieldOffsets,
    int DiscriminantSize = 0,
    int PayloadOffset = 0);

/// <summary>
/// The canonical ABI's memory layout and flattening of a <see cref="RawAdapterValueType"/>,
/// as the host's former run-time planner computed them.
/// </summary>
internal sealed class RawAdapterValuePlanner(int addressSize)
{
    private readonly string _addressType = addressSize == 8 ? "i64" : "i32";

    public int AddressSize { get; } = addressSize;

    public RawAdapterValueLayout Layout(RawAdapterValueType type) => type.Kind switch
    {
        CanonicalAbiTypeKind.Unit => new(0, 1, []),
        CanonicalAbiTypeKind.Bool or CanonicalAbiTypeKind.S8 or CanonicalAbiTypeKind.U8 => new(1, 1, []),
        CanonicalAbiTypeKind.S16 or CanonicalAbiTypeKind.U16 => new(2, 2, []),
        CanonicalAbiTypeKind.S32 or CanonicalAbiTypeKind.U32 or CanonicalAbiTypeKind.F32
            or CanonicalAbiTypeKind.Character or CanonicalAbiTypeKind.OwnedResource
            or CanonicalAbiTypeKind.BorrowedResource => new(4, 4, []),
        CanonicalAbiTypeKind.S64 or CanonicalAbiTypeKind.U64 or CanonicalAbiTypeKind.F64 => new(8, 8, []),
        CanonicalAbiTypeKind.Text or CanonicalAbiTypeKind.List => new(checked(AddressSize * 2), AddressSize, []),
        CanonicalAbiTypeKind.Alias => Layout(type.Element!),
        CanonicalAbiTypeKind.Record or CanonicalAbiTypeKind.Tuple => RecordLayout(type),
        CanonicalAbiTypeKind.Option or CanonicalAbiTypeKind.Result or CanonicalAbiTypeKind.Variant =>
            VariantLayout(type.SelectionTypes),
        CanonicalAbiTypeKind.Enum => new(DiscriminantSize(type.Cases.Length), DiscriminantSize(type.Cases.Length), []),
        CanonicalAbiTypeKind.Flags => type.Count <= 8 ? new(1, 1, [])
            : type.Count <= 16 ? new(2, 2, [])
            : new(checked((int)Math.Ceiling(type.Count / 32d) * 4), 4, []),
        _ => throw new InvalidOperationException($"Raw adapter type '{type.Kind}' is unsupported."),
    };

    public ImmutableArray<string> Flatten(RawAdapterValueType type) => type.Kind switch
    {
        CanonicalAbiTypeKind.Unit => [],
        CanonicalAbiTypeKind.S64 or CanonicalAbiTypeKind.U64 => ["i64"],
        CanonicalAbiTypeKind.F32 => ["f32"],
        CanonicalAbiTypeKind.F64 => ["f64"],
        CanonicalAbiTypeKind.Text or CanonicalAbiTypeKind.List => [_addressType, _addressType],
        CanonicalAbiTypeKind.Alias => Flatten(type.Element!),
        CanonicalAbiTypeKind.Record or CanonicalAbiTypeKind.Tuple => [.. type.Fields.SelectMany(field => Flatten(field.Type))],
        CanonicalAbiTypeKind.Option or CanonicalAbiTypeKind.Result or CanonicalAbiTypeKind.Variant =>
            FlattenVariant(type.SelectionTypes),
        CanonicalAbiTypeKind.Flags => [.. Enumerable.Repeat("i32", Math.Max(1, (int)Math.Ceiling(type.Count / 32d)))],
        _ => ["i32"],
    };

    /// <summary>A variant's flat slots after its discriminant.</summary>
    public ImmutableArray<string> JoinedSlots(ImmutableArray<RawAdapterValueType?> caseTypes) =>
        FlattenVariant(caseTypes)[1..];

    private RawAdapterValueLayout RecordLayout(RawAdapterValueType type)
    {
        var offsets = ImmutableArray.CreateBuilder<int>(type.Fields.Length);
        var offset = 0;
        var alignment = 1;
        foreach (var field in type.Fields)
        {
            var layout = Layout(field.Type);
            alignment = Math.Max(alignment, layout.Alignment);
            offset = Align(offset, layout.Alignment);
            offsets.Add(offset);
            offset = checked(offset + layout.Size);
        }
        return new(Align(offset, alignment), alignment, offsets.MoveToImmutable());
    }

    private RawAdapterValueLayout VariantLayout(ImmutableArray<RawAdapterValueType?> caseTypes)
    {
        var discriminant = DiscriminantSize(caseTypes.Length);
        var payloads = caseTypes.Where(type => type is not null).Select(type => Layout(type!)).ToArray();
        var payloadAlignment = payloads.Aggregate(1, (maximum, layout) => Math.Max(maximum, layout.Alignment));
        var payloadSize = payloads.Aggregate(0, (maximum, layout) => Math.Max(maximum, layout.Size));
        var alignment = Math.Max(discriminant, payloadAlignment);
        var payloadOffset = Align(discriminant, payloadAlignment);
        return new(
            Align(checked(payloadOffset + payloadSize), alignment),
            alignment,
            [],
            discriminant,
            payloadOffset);
    }

    private ImmutableArray<string> FlattenVariant(ImmutableArray<RawAdapterValueType?> caseTypes)
    {
        var payloads = caseTypes.Select(type => type is null ? [] : Flatten(type)).ToArray();
        var width = payloads.Aggregate(0, (maximum, payload) => Math.Max(maximum, payload.Length));
        var flattened = ImmutableArray.CreateBuilder<string>(width + 1);
        flattened.Add("i32");
        for (var index = 0; index < width; index++)
        {
            flattened.Add(payloads.Where(payload => index < payload.Length)
                .Select(payload => payload[index])
                .Aggregate(Join));
        }
        return flattened.MoveToImmutable();
    }

    private static string Join(string left, string right) =>
        left == right ? left
        : left == "i64" || right == "i64" ? "i64"
        : left is "f32" or "f64" && right is "f32" or "f64" ? "f64"
        : left is "i32" or "f32" && right is "i32" or "f32" ? "i32"
        : "i64";

    private static int DiscriminantSize(int cases) => cases <= 256 ? 1 : cases <= 65536 ? 2 : 4;

    private static int Align(int value, int alignment) => checked((value + alignment - 1) / alignment * alignment);
}
