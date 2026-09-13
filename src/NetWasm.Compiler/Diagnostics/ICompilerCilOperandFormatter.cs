using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Diagnostics;

internal interface ICompilerCilOperandFormatter
{
    string FormatOperand(CilOperand operand);
}
