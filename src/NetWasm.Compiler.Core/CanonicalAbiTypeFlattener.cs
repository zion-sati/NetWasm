using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace NetWasm.Compiler.Core;

public interface ICanonicalAbiTypeFlattener
{
    ImmutableArray<CliValueKind> Flatten(CanonicalAbiType type);
}

public sealed class CanonicalAbiTypeFlattener : ICanonicalAbiTypeFlattener
{
    public ImmutableArray<CliValueKind> Flatten(CanonicalAbiType type)
    {
        ArgumentNullException.ThrowIfNull(type);
        return type.Kind switch
        {
            CanonicalAbiTypeKind.Unit => [],
            CanonicalAbiTypeKind.Bool or
                CanonicalAbiTypeKind.S8 or
                CanonicalAbiTypeKind.U8 or
                CanonicalAbiTypeKind.S16 or
                CanonicalAbiTypeKind.U16 or
                CanonicalAbiTypeKind.S32 or
                CanonicalAbiTypeKind.U32 or
                CanonicalAbiTypeKind.Character or
                CanonicalAbiTypeKind.Enum or
                CanonicalAbiTypeKind.OwnedResource or
                CanonicalAbiTypeKind.BorrowedResource => [CliValueKind.I4],
            CanonicalAbiTypeKind.S64 or CanonicalAbiTypeKind.U64 => [CliValueKind.I8],
            CanonicalAbiTypeKind.F32 => [CliValueKind.F4],
            CanonicalAbiTypeKind.F64 => [CliValueKind.F8],
            CanonicalAbiTypeKind.Text or CanonicalAbiTypeKind.List =>
                [CliValueKind.ManagedAddress, CliValueKind.ManagedAddress],
            CanonicalAbiTypeKind.Alias => Flatten(type.ElementType!),
            CanonicalAbiTypeKind.Record or CanonicalAbiTypeKind.Tuple =>
                [.. type.Fields.SelectMany(field => Flatten(field.Type))],
            CanonicalAbiTypeKind.Option => FlattenVariant(
                [new("none", null), new("some", type.ElementType)]),
            CanonicalAbiTypeKind.Result => FlattenVariant(
                [new("ok", type.SuccessType), new("error", type.ErrorType)]),
            CanonicalAbiTypeKind.Variant => FlattenVariant(type.Cases),
            CanonicalAbiTypeKind.Flags => [.. Enumerable.Repeat(
                CliValueKind.I4,
                Math.Max(1, (type.FlagsCount + 31) / 32))],
            _ => throw new ArgumentOutOfRangeException(nameof(type), type.Kind, null),
        };
    }

    private ImmutableArray<CliValueKind> FlattenVariant(
        IEnumerable<CanonicalAbiCase> cases)
    {
        var payloads = cases.Select(@case => @case.Type is null
                ? ImmutableArray<CliValueKind>.Empty
                : Flatten(@case.Type))
            .ToArray();
        var count = payloads.Length == 0 ? 0 : payloads.Max(payload => payload.Length);
        var joined = ImmutableArray.CreateBuilder<CliValueKind>(count + 1);
        joined.Add(CliValueKind.I4);
        for (var index = 0; index < count; index++)
        {
            joined.Add(payloads
                .Where(payload => index < payload.Length)
                .Select(payload => payload[index])
                .Aggregate(Join));
        }
        return joined.ToImmutable();
    }

    private static CliValueKind Join(CliValueKind left, CliValueKind right)
    {
        if (left == right)
        {
            return left;
        }
        return (left, right) switch
        {
            (CliValueKind.ManagedAddress, CliValueKind.I4) or
                (CliValueKind.I4, CliValueKind.ManagedAddress) or
                (CliValueKind.ManagedAddress, CliValueKind.F4) or
                (CliValueKind.F4, CliValueKind.ManagedAddress) =>
                CliValueKind.ManagedAddress,
            (CliValueKind.ManagedAddress, CliValueKind.I8) or
                (CliValueKind.I8, CliValueKind.ManagedAddress) or
                (CliValueKind.ManagedAddress, CliValueKind.F8) or
                (CliValueKind.F8, CliValueKind.ManagedAddress) =>
                CliValueKind.I8,
            (CliValueKind.I4, CliValueKind.F4) or
                (CliValueKind.F4, CliValueKind.I4) => CliValueKind.I4,
            (CliValueKind.I8, CliValueKind.F4) or
                (CliValueKind.F4, CliValueKind.I8) or
                (CliValueKind.I8, CliValueKind.F8) or
                (CliValueKind.F8, CliValueKind.I8) => CliValueKind.I8,
            (CliValueKind.F4, CliValueKind.F8) or
                (CliValueKind.F8, CliValueKind.F4) => CliValueKind.F8,
            (CliValueKind.I4, CliValueKind.I8) or
                (CliValueKind.I8, CliValueKind.I4) or
                (CliValueKind.I4, CliValueKind.F8) or
                (CliValueKind.F8, CliValueKind.I4) => CliValueKind.I8,
            _ => throw new InvalidOperationException(
                $"canonical ABI cannot join '{left}' and '{right}'"),
        };
    }
}
