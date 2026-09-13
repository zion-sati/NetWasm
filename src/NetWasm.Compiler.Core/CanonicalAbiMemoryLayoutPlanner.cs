using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace NetWasm.Compiler.Core;

public sealed record CanonicalAbiFieldLayout(
    CanonicalAbiField Field,
    int Offset,
    CanonicalAbiMemoryLayout Layout);

public sealed record CanonicalAbiMemoryLayout(
    int Size,
    int Alignment,
    ImmutableArray<CanonicalAbiFieldLayout> Fields)
{
    public int DiscriminantSize { get; init; }

    public int PayloadOffset { get; init; }
}

public interface ICanonicalAbiMemoryLayoutPlanner
{
    CanonicalAbiMemoryLayout Plan(CanonicalAbiType type, WasmTarget target);
}

public sealed class CanonicalAbiMemoryLayoutPlanner :
    ICanonicalAbiMemoryLayoutPlanner
{
    public CanonicalAbiMemoryLayout Plan(CanonicalAbiType type, WasmTarget target)
    {
        ArgumentNullException.ThrowIfNull(type);
        return Plan(type, WasmTargetLayout.For(target).AddressSize);
    }

    private CanonicalAbiMemoryLayout Plan(CanonicalAbiType type, int addressSize) =>
        type.Kind switch
        {
            CanonicalAbiTypeKind.Unit => Scalar(0, 1),
            CanonicalAbiTypeKind.Bool or CanonicalAbiTypeKind.S8 or
                CanonicalAbiTypeKind.U8 => Scalar(1, 1),
            CanonicalAbiTypeKind.S16 or CanonicalAbiTypeKind.U16 => Scalar(2, 2),
            CanonicalAbiTypeKind.S32 or CanonicalAbiTypeKind.U32 or
                CanonicalAbiTypeKind.F32 or CanonicalAbiTypeKind.Character or
                CanonicalAbiTypeKind.OwnedResource or
                CanonicalAbiTypeKind.BorrowedResource => Scalar(4, 4),
            CanonicalAbiTypeKind.S64 or CanonicalAbiTypeKind.U64 or
                CanonicalAbiTypeKind.F64 => Scalar(8, 8),
            CanonicalAbiTypeKind.Text or CanonicalAbiTypeKind.List =>
                Scalar(checked(addressSize * 2), addressSize),
            CanonicalAbiTypeKind.Alias => Plan(type.ElementType!, addressSize),
            CanonicalAbiTypeKind.Record or CanonicalAbiTypeKind.Tuple =>
                Aggregate(type.Fields, addressSize),
            CanonicalAbiTypeKind.Option => Variant(
                [new("none", null), new("some", type.ElementType)], addressSize),
            CanonicalAbiTypeKind.Result => Variant(
                [new("ok", type.SuccessType), new("error", type.ErrorType)],
                addressSize),
            CanonicalAbiTypeKind.Variant => Variant(type.Cases, addressSize),
            CanonicalAbiTypeKind.Enum =>
                Scalar(DiscriminantSize(type.Cases.Length),
                    DiscriminantSize(type.Cases.Length)),
            CanonicalAbiTypeKind.Flags => Flags(type.FlagsCount),
            _ => throw new ArgumentOutOfRangeException(nameof(type), type.Kind, null),
        };

    private CanonicalAbiMemoryLayout Aggregate(
        IEnumerable<CanonicalAbiField> fields,
        int addressSize)
    {
        var layouts = ImmutableArray.CreateBuilder<CanonicalAbiFieldLayout>();
        var offset = 0;
        var alignment = 1;
        foreach (var field in fields)
        {
            var layout = Plan(field.Type, addressSize);
            alignment = Math.Max(alignment, layout.Alignment);
            offset = Align(offset, layout.Alignment);
            layouts.Add(new(field, offset, layout));
            offset = checked(offset + layout.Size);
        }
        return new(Align(offset, alignment), alignment, layouts.ToImmutable());
    }

    private CanonicalAbiMemoryLayout Variant(
        IEnumerable<CanonicalAbiCase> cases,
        int addressSize)
    {
        var caseArray = cases.ToArray();
        var discriminantSize = DiscriminantSize(caseArray.Length);
        var payloadLayouts = caseArray
            .Where(@case => @case.Type is not null)
            .Select(@case => Plan(@case.Type!, addressSize))
            .ToArray();
        var payloadAlignment = payloadLayouts.Length == 0
            ? 1
            : payloadLayouts.Max(layout => layout.Alignment);
        var payloadSize = payloadLayouts.Length == 0
            ? 0
            : payloadLayouts.Max(layout => layout.Size);
        var alignment = Math.Max(discriminantSize, payloadAlignment);
        var payloadOffset = Align(discriminantSize, payloadAlignment);
        return new(
            Align(checked(payloadOffset + payloadSize), alignment),
            alignment,
            [])
        {
            DiscriminantSize = discriminantSize,
            PayloadOffset = payloadOffset,
        };
    }

    private static CanonicalAbiMemoryLayout Flags(int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        if (count <= 8)
        {
            return Scalar(1, 1);
        }
        if (count <= 16)
        {
            return Scalar(2, 2);
        }
        return Scalar(checked(((count + 31) / 32) * 4), 4);
    }

    private static int DiscriminantSize(int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(count);
        return count <= byte.MaxValue + 1
            ? 1
            : count <= ushort.MaxValue + 1
                ? 2
                : 4;
    }

    private static CanonicalAbiMemoryLayout Scalar(int size, int alignment) =>
        new(size, alignment, []);

    private static int Align(int value, int alignment)
    {
        checked
        {
            return (value + alignment - 1) & -alignment;
        }
    }
}
