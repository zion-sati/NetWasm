// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Analysis;

// The method a call names when its operand already carries the instance, as an analyzed body's calls do; a call
// still naming a metadata token resolves to nothing, for a rule that needs no metadata.
internal sealed class OperandCalledMethodResolver : ICalledMethodResolver
{
    public static OperandCalledMethodResolver Instance { get; } = new();

    public MethodInstanceModel? Resolve(CilInstruction instruction) =>
        instruction is { Operation: CilOperation.Call or CilOperation.CallVirtual or CilOperation.NewObject, Operand: CilOperand.MethodInstance method }
            ? method.Value
            : null;
}
