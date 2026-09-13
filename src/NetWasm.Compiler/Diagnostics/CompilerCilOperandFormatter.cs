using System;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Diagnostics;

internal sealed class CompilerCilOperandFormatter : ICompilerCilOperandFormatter
{
    public string FormatOperand(CilOperand operand)
    {
        ArgumentNullException.ThrowIfNull(operand);
        return operand switch
        {
            CilOperand.MethodInstance method => method.Value.CanonicalName,
            CilOperand.FieldInstance field => field.Value.CanonicalName,
            CilOperand.TypeIdentity type => type.Value.CanonicalName,
            CilOperand.Entity entity => entity.Key.ToString(),
            _ => operand.ToString(),
        };
    }
}
