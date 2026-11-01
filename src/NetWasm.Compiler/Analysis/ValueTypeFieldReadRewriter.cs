using System;
using System.Collections.Generic;
using System.Linq;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Analysis;

internal sealed class ValueTypeFieldReadRewriter : IMethodRewriteRule
{
    public CilMethodBody Rewrite(CilMethodBody body)
    {
        ArgumentNullException.ThrowIfNull(body);

        var instructions = body.Instructions.ToArray();
        if (instructions.Length < 2)
        {
            return body;
        }

        var nonFallthroughEntries = GetNonFallthroughEntries(body);
        var changed = false;
        for (var index = 0; index + 1 < instructions.Length; index++)
        {
            var valueLoad = instructions[index];
            var fieldLoad = instructions[index + 1];
            if (fieldLoad.Operation != CilOperation.LoadField ||
                valueLoad.NextOffset != fieldLoad.Offset ||
                nonFallthroughEntries.Contains(fieldLoad.Offset) ||
                !TryGetAddressLoad(body, valueLoad, out var addressLoad))
            {
                continue;
            }

            instructions[index] = valueLoad with { Operation = addressLoad };
            changed = true;
        }

        return changed ? body with { Instructions = [.. instructions] } : body;
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
            result.Add(region.TryOffset + region.TryLength);
            result.Add(region.HandlerOffset);
            result.Add(region.HandlerOffset + region.HandlerLength);
            if (region.FilterOffset is { } filterOffset)
            {
                result.Add(filterOffset);
            }
        }

        return result;
    }

    private static bool TryGetAddressLoad(
        CilMethodBody body,
        CilInstruction instruction,
        out CilOperation operation)
    {
        operation = default;
        if (instruction.Operand is not CilOperand.Index operand)
        {
            return false;
        }

        if (instruction.Operation == CilOperation.LoadLocal)
        {
            if ((uint)operand.Value >= (uint)body.LocalSignatureTypes.Length ||
                body.LocalSignatureTypes[operand.Value].StackKind != CliValueKind.ValueType)
            {
                return false;
            }

            operation = CilOperation.LoadLocalAddress;
            return true;
        }

        if (instruction.Operation != CilOperation.LoadArgument)
        {
            return false;
        }

        var parameterIndex = operand.Value;
        if (!body.Method.IsStatic)
        {
            // A value-type instance receiver is already represented as a managed
            // address. Reference-type receivers must retain their normal load.
            if (parameterIndex == 0)
            {
                return false;
            }
            parameterIndex--;
        }

        var signature = body.MethodInstance?.Signature ?? body.Method.Signature;
        if ((uint)parameterIndex >= (uint)signature.ParameterSignatureTypes.Length ||
            signature.ParameterSignatureTypes[parameterIndex].StackKind !=
                CliValueKind.ValueType)
        {
            return false;
        }

        operation = CilOperation.LoadArgumentAddress;
        return true;
    }
}
