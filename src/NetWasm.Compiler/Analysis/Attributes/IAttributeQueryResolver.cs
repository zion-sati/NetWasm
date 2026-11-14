using System.Collections.Immutable;

namespace NetWasm.Compiler.Analysis.Attributes;

internal interface IAttributeQueryResolver
{
    // Null leaves a valid but unbounded query on its managed runtime path.
    ResolvedAttributeQuery? Resolve(AttributeQueryCall call, ImmutableArray<AttributeValueProof> stack);
}
