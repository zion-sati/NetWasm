using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Analysis;

internal interface IMethodSpecializer
{
    CilMethodBody Rewrite(CilMethodBody body);
}
