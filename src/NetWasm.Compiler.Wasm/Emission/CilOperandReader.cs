// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Wasm.Emission;

internal static class CilOperandReader
{
    public static int GetIndex(CilInstruction instruction) =>
        ((CilOperand.Index)instruction.Operand).Value;

    public static EntityKey GetEntity(CilInstruction instruction) =>
        ((CilOperand.Entity)instruction.Operand).Key;
}
