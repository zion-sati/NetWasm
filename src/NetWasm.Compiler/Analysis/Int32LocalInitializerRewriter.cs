using System.Linq;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Analysis;

internal sealed class Int32LocalInitializerRewriter : IMethodRewriteRule
{
    public CilMethodBody Rewrite(CilMethodBody body)
    {
        var instructions = body.Instructions.ToArray();
        var changed = false;
        for (var index = 0; index + 1 < instructions.Length; index++)
        {
            var address = instructions[index];
            var initialize = instructions[index + 1];
            if (address.Operation != CilOperation.LoadLocalAddress ||
                address.Operand is not CilOperand.Index local ||
                initialize.Operation != CilOperation.InitializeObject ||
                initialize.Operand is not CilOperand.TypeIdentity initializedType ||
                initializedType.Value.StackKind != CliValueKind.I4)
            {
                continue;
            }
            instructions[index] = address with
            {
                Operation = CilOperation.LoadInt32,
                Operand = new CilOperand.ConstantI4(0),
            };
            instructions[index + 1] = initialize with
            {
                Operation = CilOperation.StoreLocal,
                Operand = new CilOperand.Index(local.Value),
            };
            changed = true;
        }
        return changed ? body with { Instructions = [.. instructions] } : body;
    }
}
