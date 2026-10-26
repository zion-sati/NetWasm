using System;
using System.Collections.Immutable;
using System.Linq;
using System.Text.Json;

namespace NetWasm.Compiler.ComponentModel.Raw;

/// <summary>
/// A canonical value type as a raw adapter binding describes it: the kind tree
/// the adapter writer serializes, with record field, flag and case names already
/// in their JavaScript form.
/// </summary>
internal sealed class RawAdapterValueType
{
    public required string Kind { get; init; }

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
    public RawAdapterValueType Underlying => Kind == "alias" ? Element!.Underlying : this;

    /// <summary>The cases of an option, result or variant, each with its payload type or null.</summary>
    public ImmutableArray<RawAdapterValueType?> SelectionTypes => Kind switch
    {
        "option" => [null, Element],
        "result" => [Ok, Error],
        "variant" => [.. Cases.Select(item => item.Type)],
        _ => throw new InvalidOperationException($"'{Kind}' has no selection cases."),
    };

    /// <summary>Whether lowering a value of this type allocates guest memory.</summary>
    public bool Allocates => Kind switch
    {
        "text" or "list" => true,
        "alias" or "option" => Element!.Allocates,
        "record" or "tuple" => Fields.Any(member => member.Type.Allocates),
        "result" => Ok?.Allocates == true || Error?.Allocates == true,
        "variant" => Cases.Any(item => item.Type?.Allocates == true),
        _ => false,
    };

    /// <summary>A structural identity for sharing generated code between equal types.</summary>
    public string Key => Kind switch
    {
        "alias" or "list" or "option" => $"{Kind}({Element!.Key})",
        "record" => $"record({string.Join(",", Fields.Select(member => $"{member.JavaScriptName}:{member.Type.Key}"))})",
        "tuple" => $"tuple({string.Join(",", Fields.Select(member => member.Type.Key))})",
        "result" => $"result({Ok?.Key ?? "_"},{Error?.Key ?? "_"})",
        "variant" => $"variant({string.Join(",", Cases.Select(item => $"{item.Name}:{item.Type?.Key ?? "_"}"))})",
        "enum" => $"enum({string.Join(",", Cases.Select(item => item.Name))})",
        "flags" => $"flags({Count}:{string.Join(",", Flags)})",
        "owned-resource" or "borrowed-resource" => $"{Kind}({ResourceType})",
        _ => Kind,
    };

    public static RawAdapterValueType Read(JsonElement value)
    {
        var kind = value.GetProperty("kind").GetString()!;
        return kind switch
        {
            "alias" or "list" or "option" => new() { Kind = kind, Element = Read(value.GetProperty("element")) },
            "record" or "tuple" => new()
            {
                Kind = kind,
                Fields = [.. value.GetProperty("fields").EnumerateArray().Select(field => new RawAdapterValueField(
                    field.TryGetProperty("javascriptName", out var name) ? name.GetString() : null,
                    Read(field.GetProperty("type"))))],
            },
            "result" => new()
            {
                Kind = kind,
                Ok = ReadOptional(value.GetProperty("ok")),
                Error = ReadOptional(value.GetProperty("error")),
            },
            "variant" or "enum" => new()
            {
                Kind = kind,
                Cases = [.. value.GetProperty("cases").EnumerateArray().Select(item => new RawAdapterValueCase(
                    item.GetProperty("name").GetString()!,
                    ReadOptional(item.GetProperty("type"))))],
            },
            "flags" => new()
            {
                Kind = kind,
                Count = value.GetProperty("count").GetInt32(),
                Flags = [.. value.GetProperty("flags").EnumerateArray()
                    .Select(flag => flag.GetProperty("javascriptName").GetString()!)],
            },
            "owned-resource" or "borrowed-resource" => new()
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
        Kind = "tuple",
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
        "unit" => new(0, 1, []),
        "bool" or "s8" or "u8" => new(1, 1, []),
        "s16" or "u16" => new(2, 2, []),
        "s32" or "u32" or "f32" or "character" or "owned-resource" or "borrowed-resource" => new(4, 4, []),
        "s64" or "u64" or "f64" => new(8, 8, []),
        "text" or "list" => new(checked(AddressSize * 2), AddressSize, []),
        "alias" => Layout(type.Element!),
        "record" or "tuple" => RecordLayout(type),
        "option" or "result" or "variant" => VariantLayout(type.SelectionTypes),
        "enum" => new(DiscriminantSize(type.Cases.Length), DiscriminantSize(type.Cases.Length), []),
        "flags" => type.Count <= 8 ? new(1, 1, [])
            : type.Count <= 16 ? new(2, 2, [])
            : new(checked((int)Math.Ceiling(type.Count / 32d) * 4), 4, []),
        _ => throw new InvalidOperationException($"Raw adapter type '{type.Kind}' is unsupported."),
    };

    public ImmutableArray<string> Flatten(RawAdapterValueType type) => type.Kind switch
    {
        "unit" => [],
        "s64" or "u64" => ["i64"],
        "f32" => ["f32"],
        "f64" => ["f64"],
        "text" or "list" => [_addressType, _addressType],
        "alias" => Flatten(type.Element!),
        "record" or "tuple" => [.. type.Fields.SelectMany(field => Flatten(field.Type))],
        "option" or "result" or "variant" => FlattenVariant(type.SelectionTypes),
        "flags" => [.. Enumerable.Repeat("i32", Math.Max(1, (int)Math.Ceiling(type.Count / 32d)))],
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
