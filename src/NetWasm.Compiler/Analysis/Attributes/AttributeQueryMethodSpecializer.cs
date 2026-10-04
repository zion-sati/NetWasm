using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Analysis.Attributes;

internal sealed class AttributeQueryMethodSpecializer(
    IMethodSpecializer inner,
    IAttributeQueryLowerer queries) : IMethodSpecializer
{
    public CilMethodBody Rewrite(CilMethodBody body) => queries.Lower(inner.Rewrite(body));
}
