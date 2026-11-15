// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Metadata;

namespace NetWasm.Compiler.Analysis;

internal sealed class TypeOperandResolver(
    ITypeIdentityResolver types) : ITypeOperandResolver
{
    public CliTypeIdentity Resolve(CilInstruction instruction, MethodInstanceModel? methodInstance)
    {
        var resolved = instruction.Operand switch
        {
            CilOperand.TypeIdentity type => type.Value,
            CilOperand.ExactType type => type.Value,
            CilOperand.Entity type => types.GetTypeIdentity(type.Key),
            _ => throw new InvalidOperationException(
                $"IL_{instruction.Offset:x4} has no type operand."),
        };
        var substituted = resolved.Substitute(
            methodInstance?.DeclaringType.TypeArguments ?? [],
            methodInstance?.MethodArguments ?? []);
        if (substituted.Shape is CliTypeShape.GenericTypeParameter or CliTypeShape.GenericMethodParameter)
        {
            throw new InvalidOperationException("CIL type operand remained open after method specialization.");
        }

        return substituted;
    }
}
