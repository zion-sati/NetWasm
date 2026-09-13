using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Analysis;

internal interface ICalledMethodResolver
{
    MethodInstanceModel? Resolve(CilInstruction instruction);
}
