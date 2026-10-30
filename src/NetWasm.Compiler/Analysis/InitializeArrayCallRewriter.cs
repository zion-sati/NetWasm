using System;
using System.Linq;
using NetWasm.Compiler;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Analysis;

internal sealed class InitializeArrayCallRewriter(
    ICalledMethodResolver calledMethods,
    ISymbolFormatter symbols) : IMethodRewriteRule
{
    public CilMethodBody Rewrite(CilMethodBody body)
    {
        var instructions = body.Instructions.ToArray();
        var changed = false;
        for (var index = 1; index < instructions.Length; index++)
        {
            var call = instructions[index];
            var token = instructions[index - 1];
            if (call.Operation != CilOperation.Call ||
                token.Operation != CilOperation.LoadFieldToken)
            {
                continue;
            }
            var target = calledMethods.Resolve(call);
            if (target is null ||
                symbols.Format(target.Definition.DeclaringType) !=
                    "System.Runtime.CompilerServices.RuntimeHelpers" ||
                target.Definition.Name != "InitializeArray")
            {
                continue;
            }
            if (token.Operand is not CilOperand.FieldInstance { Value: var field } ||
                field.Definition.InitialData.IsDefaultOrEmpty)
            {
                throw new CompilerException(new CompilerDiagnostic(
                    DiagnosticCode.UnsupportedMetadata,
                    "RuntimeHelpers.InitializeArray requires field RVA data"));
            }
            instructions[index - 1] = token with
            {
                Operation = CilOperation.Nop,
                Operand = new CilOperand.None(),
            };
            instructions[index] = call with
            {
                Operation = CilOperation.InitializeArrayData,
                Operand = new CilOperand.ArrayInitializerData(field),
            };
            changed = true;
        }
        return changed ? body with { Instructions = [.. instructions] } : body;
    }
}
