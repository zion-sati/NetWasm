using System;
using System.Collections.Generic;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Wasm.Emission.Instructions;

internal static class NumericStackTypes
{
    public static CliValueKind RequireMatching(
        List<CliValueKind> stack,
        int left)
    {
        var type = stack[left];
        if (type != stack[left + 1] || !IsNumeric(type))
        {
            throw new InvalidOperationException(
                "numeric operation has incompatible stack types");
        }
        return type;
    }

    public static CliValueKind GetCompatible(
        CliValueKind left,
        CliValueKind right)
    {
        if (left == right && IsNumeric(left))
        {
            return left;
        }
        if (left is CliValueKind.NativeInt && right is CliValueKind.I4 ||
            left is CliValueKind.I4 && right is CliValueKind.NativeInt)
        {
            return CliValueKind.NativeInt;
        }
        throw new InvalidOperationException(
            "numeric operation has incompatible stack types");
    }

    public static CliValueKind GetAddCompatible(
        CliValueKind left,
        CliValueKind right)
    {
        if (left == CliValueKind.ManagedAddress && IsAddressOffset(right) ||
            right == CliValueKind.ManagedAddress && IsAddressOffset(left))
        {
            return CliValueKind.ManagedAddress;
        }
        return GetCompatible(left, right);
    }

    public static CliValueKind GetSubtractCompatible(
        CliValueKind left,
        CliValueKind right)
    {
        if (left == CliValueKind.ManagedAddress && IsAddressOffset(right))
        {
            return CliValueKind.ManagedAddress;
        }
        if (left == CliValueKind.ManagedAddress && right == CliValueKind.ManagedAddress)
        {
            return CliValueKind.NativeInt;
        }
        return GetCompatible(left, right);
    }

    private static bool IsAddressOffset(CliValueKind type) =>
        type is CliValueKind.I4 or CliValueKind.NativeInt;

    private static bool IsNumeric(CliValueKind type) => type is
        CliValueKind.I4 or CliValueKind.I8 or CliValueKind.NativeInt or
        CliValueKind.F4 or CliValueKind.F8;
}
