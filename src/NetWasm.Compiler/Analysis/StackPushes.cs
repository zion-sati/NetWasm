// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Analysis;

// How many values an instruction other than dup leaves on top of what it consumed, one or none: a call by what its
// method returns, and none for a store, branch or the end of a block. The stack heights around it then decide what
// it consumed.
internal static class StackPushes
{
    public static int Of(CilInstruction instruction, ICalledMethodResolver calls) => instruction.Operation switch
    {
        CilOperation.Call or CilOperation.CallVirtual =>
            calls.Resolve(instruction)?.Signature.ReturnType == CliValueKind.Void ? 0 : 1,
        CilOperation.CallIndirect =>
            ((CilOperand.CallSite)instruction.Operand).Signature.ReturnType == CliValueKind.Void ? 0 : 1,
        _ => instruction.Operation is CilOperation.Nop or CilOperation.Pop or CilOperation.StoreLocal or
            CilOperation.StoreArgument or CilOperation.StoreField or CilOperation.StoreStaticField or
            CilOperation.StoreObject or CilOperation.StoreArrayElement or CilOperation.StoreArrayElementReference or
            CilOperation.StoreRectangularArrayElement or CilOperation.CopyObject or CilOperation.InitializeObject or
            CilOperation.CopyBlock or CilOperation.InitializeBlock or CilOperation.InitializeArrayData or
            CilOperation.Branch or CilOperation.BranchIfTrue or CilOperation.BranchIfFalse or
            CilOperation.BranchIfEqual or CilOperation.BranchIfNotEqual or
            CilOperation.BranchIfGreaterThanSigned or CilOperation.BranchIfGreaterThanUnsigned or
            CilOperation.BranchIfGreaterThanOrEqualSigned or CilOperation.BranchIfGreaterThanOrEqualUnsigned or
            CilOperation.BranchIfLessThanSigned or CilOperation.BranchIfLessThanUnsigned or
            CilOperation.BranchIfLessThanOrEqualSigned or CilOperation.BranchIfLessThanOrEqualUnsigned or
            CilOperation.Constrained or CilOperation.Volatile or CilOperation.Readonly or CilOperation.Unaligned or
            CilOperation.Break or CilOperation.Throw or CilOperation.Rethrow or CilOperation.Leave or
            CilOperation.EndFinally or CilOperation.EndFilter or CilOperation.Return or CilOperation.Switch
                ? 0
                : 1,
    };
}
