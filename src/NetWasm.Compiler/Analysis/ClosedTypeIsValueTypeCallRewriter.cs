using System;
using System.Collections.Generic;
using System.Linq;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Analysis;

internal sealed class ClosedTypeIsValueTypeCallRewriter(
    ICalledMethodResolver calledMethods,
    ISymbolFormatter symbols) : IMethodRewriteRule
{
    public CilMethodBody Rewrite(CilMethodBody body)
    {
        ArgumentNullException.ThrowIfNull(body);

        var instructions = body.Instructions.ToArray();
        var entryOffsets = GetNonFallthroughEntries(body);
        var changed = false;
        for (var index = 2; index < instructions.Length; index++)
        {
            var token = instructions[index - 2];
            var materialization = instructions[index - 1];
            var call = instructions[index];
            if (token.Operation != CilOperation.LoadTypeToken ||
                token.Operand is not CilOperand.TypeIdentity { Value: var type } ||
                type.ContainsGenericParameters ||
                materialization.Operation != CilOperation.MaterializeType ||
                call.Operation is not (CilOperation.Call or CilOperation.CallVirtual) ||
                token.NextOffset != materialization.Offset ||
                materialization.NextOffset != call.Offset ||
                entryOffsets.Contains(materialization.Offset) ||
                entryOffsets.Contains(call.Offset) ||
                !IsValueTypeGetter(call))
            {
                continue;
            }

            instructions[index - 2] = token with
            {
                Operation = CilOperation.Nop,
                Operand = new CilOperand.None(),
            };
            instructions[index - 1] = materialization with
            {
                Operation = CilOperation.Nop,
                Operand = new CilOperand.None(),
            };
            instructions[index] = call with
            {
                Operation = CilOperation.LoadInt32,
                Operand = new CilOperand.ConstantI4(type.IsValueType ? 1 : 0),
            };
            changed = true;
        }

        return changed ? body with { Instructions = [.. instructions] } : body;
    }

    private bool IsValueTypeGetter(CilInstruction call)
    {
        var target = calledMethods.Resolve(call);
        return target is not null &&
            !target.Definition.IsStatic &&
            target.Definition.Name == "get_IsValueType" &&
            symbols.Format(target.Definition.DeclaringType) == "System.Type" &&
            target.Signature.ReturnSignatureType.StackKind == CliValueKind.I4 &&
            target.Signature.ParameterSignatureTypes.IsEmpty;
    }

    private static HashSet<int> GetNonFallthroughEntries(CilMethodBody body)
    {
        var result = new HashSet<int>();
        foreach (var instruction in body.Instructions)
        {
            if (instruction.Operand is CilOperand.BranchTarget target)
            {
                result.Add(target.Offset);
            }
        }

        foreach (var region in body.ExceptionRegions)
        {
            result.Add(region.TryOffset);
            result.Add(checked(region.TryOffset + region.TryLength));
            result.Add(region.HandlerOffset);
            result.Add(checked(region.HandlerOffset + region.HandlerLength));
            if (region.FilterOffset is { } filterOffset)
            {
                result.Add(filterOffset);
            }
        }

        return result;
    }
}
