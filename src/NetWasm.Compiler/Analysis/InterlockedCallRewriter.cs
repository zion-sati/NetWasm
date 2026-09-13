using System;
using System.Linq;
using NetWasm.Compiler;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Analysis;

internal sealed class InterlockedCallRewriter(
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
                symbols.Format(target.Definition.DeclaringType) !=
                    "System.Threading.Interlocked" ||
                target.Definition.Name != "CompareExchange")
            {
                continue;
            }
            if (target.MethodArguments.Length != 1 ||
                target.MethodArguments[0].Shape is CliTypeShape.GenericMethodParameter or
                    CliTypeShape.GenericTypeParameter)
            {
                throw new CompilerException(new CompilerDiagnostic(
                    DiagnosticCode.UnsupportedMetadata,
                    "Interlocked.CompareExchange requires one closed type argument"));
            }
            instructions[index] = call with
            {
                Operation = CilOperation.CompareExchange,
                Operand = new CilOperand.TypeIdentity(target.MethodArguments[0]),
            };
            changed = true;
        }
        return changed ? body with { Instructions = [.. instructions] } : body;
    }
}
