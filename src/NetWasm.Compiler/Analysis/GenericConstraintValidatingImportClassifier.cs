using System;
using NetWasm.Compiler.Core.GenericConstraints;

namespace NetWasm.Compiler.Analysis;

internal sealed class GenericConstraintValidatingImportClassifier(
    IReachabilityImportClassifier inner,
    IClosedGenericConstraintValidator constraints) : IReachabilityImportClassifier
{
    private readonly IReachabilityImportClassifier _inner = inner ?? throw new ArgumentNullException(nameof(inner));
    private readonly IClosedGenericConstraintValidator _constraints = constraints ?? throw new ArgumentNullException(nameof(constraints));

    public ReachabilityImportAnalysis Classify(ReachabilityImportRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        _constraints.Validate(request.Method);
        return _inner.Classify(request);
    }
}
