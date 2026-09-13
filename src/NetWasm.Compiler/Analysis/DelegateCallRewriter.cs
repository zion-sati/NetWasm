using System.Linq;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Analysis;

internal sealed class DelegateCallRewriter(
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
            if (call.Operation != CilOperation.Call)
            {
                continue;
            }
            var target = calledMethods.Resolve(call);
            if (target is null ||
                symbols.Format(target.Definition.DeclaringType) != "System.Delegate")
            {
                continue;
            }
            var replacement = target.Definition.Name switch
            {
                "Combine" => CilOperation.DelegateCombine,
                "Remove" => CilOperation.DelegateRemove,
                "op_Equality" => CilOperation.DelegateEqual,
                "op_Inequality" => CilOperation.DelegateNotEqual,
                _ => (CilOperation?)null,
            };
            if (replacement is null)
            {
                continue;
            }
            instructions[index] = call with
            {
                Operation = replacement.Value,
                Operand = new CilOperand.None(),
            };
            changed = true;
        }
        return changed ? body with { Instructions = [.. instructions] } : body;
    }
}
