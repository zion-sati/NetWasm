// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Analysis.NullChecks;

// O0 checks the address of each access through a managed address and throws NullReferenceException where it is 0, so
// code that reads or writes a run of elements without those checks (a vector loop, the fold of a range) may only run
// where no address in the run is 0. The run is a first element's address and a count of further elements of one size:
// no address in it is 0 when the first is not and the last does not wrap past the top of the address space, which a
// span over a null reference, or one made from an arbitrary address, can do. Where that does not hold, a rewrite takes
// its original code, whose checks throw where O0's do.
internal static class NonZeroAddressRange
{
    // Pushes 1 (an int) when the run may hold address 0, else 0: count + 1 > (0 - first) / size, in native unsigned
    // arithmetic, which holds on wasm32 and wasm64 alike. 0 - first is 0 when first is, so a run from address 0 fails;
    // otherwise it is the room above first, and count + 1 elements fit there only when the last ends below the top of
    // the address space, one element short of it at most. first pushes the run's first managed address and count the
    // number of elements past it, a non-negative int32; both are pure. The value takes no branch, so a rewrite that tests
    // several runs branches once.
    public static void PushMayHoldZero(Action<CilOperation, CilOperand?> add, Action first, Action count, CliTypeIdentity element)
    {
        ArgumentNullException.ThrowIfNull(add);
        ArgumentNullException.ThrowIfNull(first);
        ArgumentNullException.ThrowIfNull(count);
        count();
        add(CilOperation.ConvertNativeUInt, null);
        add(CilOperation.LoadInt32, new CilOperand.ConstantI4(1));
        add(CilOperation.ConvertNativeUInt, null);
        add(CilOperation.Add, null);
        add(CilOperation.LoadInt32, new CilOperand.ConstantI4(0));
        add(CilOperation.ConvertNativeUInt, null);
        first();
        add(CilOperation.ConvertNativeUInt, null);
        add(CilOperation.Subtract, null);
        add(CilOperation.SizeOf, new CilOperand.TypeIdentity(element));
        add(CilOperation.DivideUnsigned, null);
        add(CilOperation.CompareGreaterThanUnsigned, null);
    }

    // Branches to failure when the run may hold address 0.
    public static void BranchUnlessNonZero(
        Action<CilOperation, CilOperand?> add,
        Action first,
        Action count,
        CliTypeIdentity element,
        CilOperand.BranchTarget failure)
    {
        ArgumentNullException.ThrowIfNull(failure);
        PushMayHoldZero(add, first, count, element);
        add(CilOperation.BranchIfTrue, failure);
    }
}
