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
                ("System.Type", "GetTypeFromHandle") => CilOperation.MaterializeType,
                ("System.Object", "GetType") => CilOperation.GetObjectType,
                _ => (CilOperation?)null,
            };
            if (replacement is null)
            {
                continue;
            }
            instructions[index] = call with
            {
                Operation = replacement.Value,
                Operand = replacement == CilOperation.GetObjectType &&
                    index > 0 &&
                    instructions[index - 1].Operation == CilOperation.Constrained &&
                    instructions[index - 1].Operand is CilOperand.TypeIdentity constrained
                        ? constrained
                        : new CilOperand.None(),
            };
            changed = true;
        }
        return changed ? body with { Instructions = [.. instructions] } : body;
    }
}
