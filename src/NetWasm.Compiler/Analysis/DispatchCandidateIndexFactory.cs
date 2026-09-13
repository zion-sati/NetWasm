using System;

namespace NetWasm.Compiler.Analysis;

internal sealed class DispatchCandidateIndexFactory(
    ITypeRelationshipClassifier relationships) : IDispatchCandidateIndexFactory
{
    private readonly ITypeRelationshipClassifier _relationships =
        relationships ?? throw new ArgumentNullException(nameof(relationships));

    public IDispatchCandidateIndex Create() =>
        new DispatchCandidateIndex(_relationships);
}
