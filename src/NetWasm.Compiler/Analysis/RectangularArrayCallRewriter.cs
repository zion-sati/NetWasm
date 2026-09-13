using System;
using System.Linq;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Analysis;

internal sealed class RectangularArrayCallRewriter : IMethodRewriteRule
{
    public CilMethodBody Rewrite(CilMethodBody body)
    {
        ArgumentNullException.ThrowIfNull(body);
        return body with
        {
            Instructions = [.. body.Instructions.Select(Rewrite)],
        };
    }

    private static CilInstruction Rewrite(CilInstruction instruction)
    {
        if (instruction.Operand is not CilOperand.MethodInstance { Value: var method } ||
            method.DeclaringType.Shape != CliTypeShape.Array)
        {
            return instruction;
        }

        var operation = (instruction.Operation, method.Definition.Name) switch
        {
            (CilOperation.NewObject, ".ctor") => CilOperation.NewRectangularArray,
            (CilOperation.Call, "Get") => CilOperation.LoadRectangularArrayElement,
            (CilOperation.Call, "Set") => CilOperation.StoreRectangularArrayElement,
            (CilOperation.Call, "Address") =>
                CilOperation.LoadRectangularArrayElementAddress,
            _ => throw new CompilerException(new CompilerDiagnostic(
                DiagnosticCode.UnsupportedCil,
                $"runtime-provided array member '{method.DeclaringType}::" +
                $"{method.Definition.Name}' cannot be lowered from " +
                $"'{instruction.Operation}'",
                IlOffset: instruction.Offset)),
        };
        return instruction with
        {
            Operation = operation,
            Operand = new CilOperand.TypeIdentity(method.DeclaringType),
        };
    }
}
