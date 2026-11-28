// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Analysis.RuntimeAsync;

// A lowered method's request, right before a virtual or interface call it awaits, that the override which runs take the
// call directly (RuntimeAsync.CallDirectVirtual). The call it precedes stays that call: inlining a callee there would
// hand the request to whatever the callee's body calls first (VirtualDirectCallProgramReducer gives the call's
// overrides that are not lowered their declining start).
internal static class RuntimeAsyncVirtualRequest
{
    public const string RuntimeAsyncName = "System.Runtime.CompilerServices.RuntimeAsync";

    public static bool Is(CilInstruction instruction) => IsRuntimeAsyncCall(instruction, "CallDirectVirtual");

    public static bool IsRuntimeAsyncCall(CilInstruction instruction, string name) =>
        instruction is { Operation: CilOperation.Call, Operand: CilOperand.MethodInstance { Value: var method } } &&
        method.Definition.Name == name && method.DeclaringType.FullName == RuntimeAsyncName;
}
