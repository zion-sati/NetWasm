// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Wasm.Emission;

internal sealed class CilTypeOperandResolver(
    ICilTypeIdentityResolver identities) : ICilTypeOperandResolver
{
    public CliTypeIdentity Resolve(CilInstruction instruction, MethodInstanceModel? methodInstance)
    {
        var type = instruction.Operand switch
        {
            CilOperand.TypeIdentity identity => identity.Value,
            CilOperand.ExactType exact => exact.Value,
            CilOperand.Entity entity => identities.Resolve(entity.Key),
            _ => throw new InvalidOperationException(
                $"instruction {instruction.Operation} has no type operand"),
        };
        var substituted = type.Substitute(
            methodInstance?.DeclaringType.TypeArguments ?? [],
            methodInstance?.MethodArguments ?? []);
        if (substituted.Shape is CliTypeShape.GenericTypeParameter or CliTypeShape.GenericMethodParameter)
        {
            throw new InvalidOperationException("CIL type operand remained open after method specialization.");
        }

        return substituted;
    }
}
