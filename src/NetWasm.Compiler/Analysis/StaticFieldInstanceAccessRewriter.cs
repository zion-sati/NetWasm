// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Collections.Generic;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Analysis;

// ldfld, ldflda and stfld may name a static field (ECMA-335 III.4.10, 4.11 and 4.28): CoreCLR evaluates the object,
// drops it without checking it for null, and reads, addresses or writes the static, as Avalonia's XAML compiler
// relies on when it loads a routed event field from the control it wires a handler on. Each such instruction becomes
// its static form with the object popped: `pop; ldsfld`, `pop; ldsflda`, and `stsfld; pop`, the stored value being
// above the object. A prefix stays immediately before the field instruction it marks.
internal sealed class StaticFieldInstanceAccessRewriter(IFieldRepository fields) : IMethodRewriteRule
{
    private readonly IFieldRepository _fields = fields ?? throw new ArgumentNullException(nameof(fields));

    public CilMethodBody Rewrite(CilMethodBody body)
    {
        ArgumentNullException.ThrowIfNull(body);
        List<CilInstruction>[]? replacements = null;
        for (var index = 0; index < body.Instructions.Length; index++)
        {
            var instruction = body.Instructions[index];
            if (instruction.Operation is not (CilOperation.LoadField or CilOperation.LoadFieldAddress or
                    CilOperation.StoreField) ||
                !IsStatic(instruction.Operand))
            {
                continue;
            }
            replacements ??= Unchanged(body);
            var pop = new CilInstruction(0, 1, CilOperation.Pop, new CilOperand.None());
            if (instruction.Operation == CilOperation.StoreField)
            {
                replacements[index] = [instruction with { Operation = CilOperation.StoreStaticField }, pop];
                continue;
            }
            replacements[index] = [instruction with
            {
                Operation = instruction.Operation == CilOperation.LoadField
                    ? CilOperation.LoadStaticField
                    : CilOperation.LoadStaticFieldAddress,
            }];
            // The object goes before the prefixes that mark the field instruction.
            var first = index;
            while (first > 0 && IsPrefix(body.Instructions[first - 1].Operation)) first--;
            replacements[first].Insert(0, pop);
        }
        return replacements is null ? body : RenumberedInstructions.Splice(body, replacements);
    }

    // A field instruction names its field by a field instance or by its token.
    private bool IsStatic(CilOperand operand) => operand is CilOperand.FieldInstance { Value: var field }
        ? field.Definition.IsStatic
        : _fields.GetField(((CilOperand.Entity)operand).Key).IsStatic;

    private static bool IsPrefix(CilOperation operation) =>
        operation is CilOperation.Volatile or CilOperation.Unaligned;

    private static List<CilInstruction>[] Unchanged(CilMethodBody body)
    {
        var replacements = new List<CilInstruction>[body.Instructions.Length];
        for (var index = 0; index < replacements.Length; index++) replacements[index] = [body.Instructions[index]];
        return replacements;
    }
}
