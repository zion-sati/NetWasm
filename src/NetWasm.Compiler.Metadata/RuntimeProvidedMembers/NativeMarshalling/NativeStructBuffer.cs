// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using NetWasm.Compiler.Core;
using static NetWasm.Compiler.Metadata.RuntimeProvidedMembers.CilBodyElements;

namespace NetWasm.Compiler.Metadata.RuntimeProvidedMembers.NativeMarshalling;

// The 16-byte-aligned buffer a marshalling stub allocates on its stack for a struct's native copy (INativeStructCopier).
internal static class NativeStructBuffer
{
    // Allocates a buffer of the native copy's size into the local, zeroed when nothing is copied into it first.
    public static ImmutableArray<CilBodyElement> Allocate(CilBodyLocal buffer, int nativeSize, bool zero)
    {
        var size = Math.Max(nativeSize, 1);
        var elements = new List<CilBodyElement>
        {
            Constant(size + 15),
            Op(CilOperation.ConvertNativeUInt),
            Op(CilOperation.LocalAllocate),
            Constant(15),
            Op(CilOperation.ConvertNativeInt),
            Op(CilOperation.Add),
            Constant(-16),
            Op(CilOperation.ConvertNativeInt),
            Op(CilOperation.BitwiseAnd),
            Store(buffer),
        };
        if (zero)
        {
            elements.AddRange([Load(buffer), Constant(0), Constant(size), Op(CilOperation.InitializeBlock)]);
        }
        return [.. elements];
    }

    private static CilBodyElement Constant(int value) => Op(CilOperation.LoadInt32, new CilOperand.ConstantI4(value));
}
