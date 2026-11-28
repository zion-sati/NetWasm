// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Linq;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Analysis.Constants;

// Boxing a reference type does nothing (ECMA-335 III.4.1): the value is already the object. Generic code leaves such a
// box wherever it compares a value of its type parameter with null (`value is null` is `box T; brtrue`), and once T is
// a class it is only an instruction the root maps count as an allocation, so a reference live across it needs a root
// frame no allocation does. It goes, and the reference stays on the stack as it was.
internal sealed class ReferenceTypeBoxRemover(ITypeDefinitionResolver typeDefinitions) : IMethodRewriteRule
{
    private readonly ITypeDefinitionResolver _typeDefinitions = typeDefinitions ??
        throw new ArgumentNullException(nameof(typeDefinitions));

    public CilMethodBody Rewrite(CilMethodBody body)
    {
        ArgumentNullException.ThrowIfNull(body);
        if (!body.Instructions.Any(IsReferenceTypeBox)) return body;
        return body with
        {
            Instructions =
            [
                .. body.Instructions.Select(instruction => IsReferenceTypeBox(instruction)
                    ? instruction with { Operation = CilOperation.Nop, Operand = new CilOperand.None() }
                    : instruction),
            ],
        };
    }

    // String or object, an array, or a class or interface by its definition; a value type, a nullable, a pointer or a
    // type still open boxes as it says.
    private bool IsReferenceTypeBox(CilInstruction instruction) =>
        instruction is { Operation: CilOperation.Box, Operand: CilOperand.TypeIdentity { Value: var type } } &&
        !type.ContainsGenericParameters && type.StackKind == CliValueKind.ManagedReference && type.Shape switch
        {
            CliTypeShape.Primitive or CliTypeShape.SzArray or CliTypeShape.Array => true,
            CliTypeShape.Named or CliTypeShape.GenericInstantiation => !_typeDefinitions.ResolveTypeIdentity(type).IsValueType,
            _ => false,
        };
}
