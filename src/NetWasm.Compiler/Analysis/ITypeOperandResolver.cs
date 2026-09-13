using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Analysis;

internal interface ITypeOperandResolver
{
    CliTypeIdentity Resolve(CilInstruction instruction, MethodInstanceModel? methodInstance);
}
