using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Analysis;

internal interface IMethodRewriteRule
{
    CilMethodBody Rewrite(CilMethodBody body);
}
