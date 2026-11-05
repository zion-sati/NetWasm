using System;

namespace NetWasm.Compiler.Analysis;

internal sealed class DispatchCandidateIndexFactory(
    ITypeRelationshipClassifier relationships,
    ITypeRelationshipKeyResolver relationshipKeys) : IDispatchCandidateIndexFactory
{
    private readonly ITypeRelationshipClassifier _relationships =
        relationships ?? throw new ArgumentNullException(nameof(relationships));
    private readonly ITypeRelationshipKeyResolver _relationshipKeys =
        relationshipKeys ?? throw new ArgumentNullException(nameof(relationshipKeys));

    public IDispatchCandidateIndex Create() =>
        new DispatchCandidateIndex(_relationships, _relationshipKeys);
}
