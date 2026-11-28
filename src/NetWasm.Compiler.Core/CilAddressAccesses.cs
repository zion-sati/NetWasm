// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Immutable;

namespace NetWasm.Compiler.Core;

// The instructions that read or write through an address the emitters check for null, and how deep beneath the top of
// the stack each such address lies (1 is the top). Where the operand is a managed address rather than an object, a
// value or an unmanaged pointer, a null one throws NullReferenceException at the access.
public static class CilAddressAccesses
{
    private static readonly int[] Top = [1];
    private static readonly int[] UnderValue = [2];
    // The destination, then the source.
    private static readonly int[] DestinationAndSource = [2, 1];
    // The location, beneath the value and the comparand.
    private static readonly int[] UnderTwo = [3];

    public static int[] Depths(CilOperation operation) => operation switch
    {
        CilOperation.LoadObject or CilOperation.InitializeObject or
            CilOperation.LoadField or CilOperation.LoadFieldAddress => Top,
        CilOperation.StoreObject or CilOperation.StoreField => UnderValue,
        CilOperation.CopyObject => DestinationAndSource,
        CilOperation.CompareExchange => UnderTwo,
        _ => [],
    };

    // Whether the access at the index carries an unaligned. prefix, perhaps among others. Such an access reads or writes
    // raw memory without a null check, as Unsafe.ReadUnaligned and WriteUnaligned do on Mono AOT (a read of address 0
    // through them returns what is there): it neither throws at address 0 nor shows its address non-null.
    public static bool IsUnaligned(ImmutableArray<CilInstruction> instructions, int index)
    {
        for (var previous = index - 1; previous >= 0; previous--)
        {
            switch (instructions[previous].Operation)
            {
                case CilOperation.Unaligned:
                    return true;
                case CilOperation.Volatile or CilOperation.Readonly or CilOperation.Constrained:
                    continue;
                default:
                    return false;
            }
        }
        return false;
    }
}
