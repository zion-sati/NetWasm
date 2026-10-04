using System.Collections.Immutable;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Analysis.Attributes;

internal interface IAttributeQueryBodyRewriter
{
    CilMethodBody Rewrite(
        CilMethodBody body,
        ImmutableDictionary<int, AttributeCilFragment> replacements);
}
