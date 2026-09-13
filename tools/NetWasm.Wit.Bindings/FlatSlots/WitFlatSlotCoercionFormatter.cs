using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using NetWasm.Compiler.Core;

namespace NetWasm.Wit.Bindings.FlatSlots;

public sealed class WitFlatSlotCoercionFormatter : IWitFlatSlotCoercionFormatter
{
    private static readonly FrozenDictionary<
        (CliValueKind Source, CliValueKind Destination),
        Func<string, string>> Coercions =
        new Dictionary<
            (CliValueKind Source, CliValueKind Destination),
            Func<string, string>>
        {
            [(CliValueKind.F4, CliValueKind.I4)] = value =>
                $"CanonicalAbi.SingleToInt32Bits(({value}))",
            [(CliValueKind.I4, CliValueKind.F4)] = value =>
                $"CanonicalAbi.Int32BitsToSingle(({value}))",
            [(CliValueKind.I4, CliValueKind.I8)] = value =>
                $"unchecked((long)(uint)({value}))",
            [(CliValueKind.F4, CliValueKind.I8)] = value =>
                $"unchecked((long)(uint)CanonicalAbi.SingleToInt32Bits(({value})))",
            [(CliValueKind.I8, CliValueKind.I4)] = value =>
                $"unchecked((int)({value}))",
            [(CliValueKind.I8, CliValueKind.F4)] = value =>
                $"CanonicalAbi.Int32BitsToSingle(unchecked((int)({value})))",
            [(CliValueKind.F8, CliValueKind.I8)] = value =>
                $"CanonicalAbi.DoubleToInt64Bits(({value}))",
            [(CliValueKind.I8, CliValueKind.F8)] = value =>
                $"CanonicalAbi.Int64BitsToDouble(({value}))",
            [(CliValueKind.F4, CliValueKind.F8)] = value => $"(double)({value})",
            [(CliValueKind.F8, CliValueKind.F4)] = value => $"(float)({value})",
            [(CliValueKind.ManagedAddress, CliValueKind.I8)] = value =>
                $"unchecked((long)({value}))",
            [(CliValueKind.I8, CliValueKind.ManagedAddress)] = value =>
                $"unchecked((nuint)({value}))",
            [(CliValueKind.I4, CliValueKind.ManagedAddress)] = value =>
                $"unchecked((nuint)(uint)({value}))",
            [(CliValueKind.ManagedAddress, CliValueKind.I4)] = value =>
                $"unchecked((int)({value}))",
            [(CliValueKind.F4, CliValueKind.ManagedAddress)] = value =>
                $"unchecked((nuint)(uint)CanonicalAbi.SingleToInt32Bits(({value})))",
            [(CliValueKind.ManagedAddress, CliValueKind.F4)] = value =>
                $"CanonicalAbi.Int32BitsToSingle(unchecked((int)({value})))",
        }.ToFrozenDictionary();

    private static readonly FrozenDictionary<CliValueKind, string> RawTypes =
        new Dictionary<CliValueKind, string>
        {
            [CliValueKind.I4] = "int",
            [CliValueKind.I8] = "long",
            [CliValueKind.F4] = "float",
            [CliValueKind.F8] = "double",
            [CliValueKind.ManagedAddress] = "nuint",
        }.ToFrozenDictionary();

    public string Format(WitFlatSlotCoercionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Expression);

        if (request.Source == request.Destination &&
            RawTypes.TryGetValue(request.Destination, out var rawType))
        {
            return $"unchecked(({rawType})({request.Expression}))";
        }

        if (Coercions.TryGetValue(
                (request.Source, request.Destination),
                out var format))
        {
            return format(request.Expression);
        }

        throw new ArgumentOutOfRangeException(
            nameof(request),
            "The flat-slot coercion is not supported.");
    }
}
