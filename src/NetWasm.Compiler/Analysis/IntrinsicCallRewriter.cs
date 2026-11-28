// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Linq;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Analysis;

internal sealed class IntrinsicCallRewriter(
    ICalledMethodResolver calledMethods,
    ISymbolFormatter symbols) : IMethodRewriteRule
{
    public CilMethodBody Rewrite(CilMethodBody body)
    {
        var instructions = body.Instructions.ToArray();
        var changed = false;
        for (var index = 0; index < instructions.Length; index++)
        {
            var call = instructions[index];
            if (call.Operation is not (CilOperation.Call or CilOperation.CallVirtual))
            {
                continue;
            }
            var target = calledMethods.Resolve(call);
            if (target is null)
            {
                continue;
            }
            var replacement = (symbols.Format(target.Definition.DeclaringType),
                target.Definition.Name) switch
            {
                ("System.Type", "GetTypeFromHandle" or "GetTypeFromSemanticId") =>
                    CilOperation.MaterializeType,
                ("System.Type", "InternalGetFacts") when IsTypeFactsLookup(target) =>
                    CilOperation.GetTypeFacts,
                ("System.Reflection.MethodBase", "GetMethodFromHandle") =>
                    CilOperation.MaterializeMethod,
                ("System.Reflection.FieldInfo", "GetFieldFromHandle") =>
                    CilOperation.MaterializeField,
                ("System.Object", "GetType") => CilOperation.GetObjectType,
                _ => (CilOperation?)null,
            };
            if (replacement is null)
            {
                continue;
            }
            // A constrained GetType becomes GetObjectType of the constrained type, which takes the prefix's managed
            // pointer itself: the prefix goes, as ConstrainedCallRewriter drops it, so the next call does not take it.
            var constrained = replacement == CilOperation.GetObjectType && index > 0 &&
                instructions[index - 1] is { Operation: CilOperation.Constrained, Operand: CilOperand.TypeIdentity prefix }
                    ? prefix
                    : null;
            if (constrained is not null)
            {
                instructions[index - 1] = instructions[index - 1] with
                {
                    Operation = CilOperation.Nop,
                    Operand = new CilOperand.None(),
                };
            }
            instructions[index] = call with
            {
                Operation = replacement.Value,
                Operand = replacement switch
                {
                    CilOperation.GetObjectType when constrained is not null => constrained,
                    CilOperation.MaterializeMethod or CilOperation.MaterializeField =>
                        new CilOperand.Index(target.Signature.ParameterTypes.Length),
                    _ => new CilOperand.None(),
                },
            };
            changed = true;
        }
        return changed ? body with { Instructions = [.. instructions] } : body;
    }

    private static bool IsTypeFactsLookup(MethodInstanceModel method) =>
        method.Definition.IsStatic &&
        method.Signature.ReturnSignatureType.StackKind == CliValueKind.NativeInt &&
        method.Signature.ParameterSignatureTypes.IsEmpty;
}
