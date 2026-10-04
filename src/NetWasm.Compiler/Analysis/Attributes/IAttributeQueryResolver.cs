using System.Collections.Immutable;

namespace NetWasm.Compiler.Analysis.Attributes;

internal interface IAttributeQueryResolver
{
    ResolvedAttributeQuery Resolve(AttributeQueryCall call, ImmutableArray<AttributeValueProof> stack);
}
