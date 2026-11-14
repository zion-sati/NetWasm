using System;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Analysis;

internal sealed class DiagnosticReachabilityImportClassifier(
    IReachabilityImportClassifier inner,
    ICompilerDiagnosticProvenanceBinder provenance) : IReachabilityImportClassifier
{
    private readonly IReachabilityImportClassifier _inner = inner ?? throw new ArgumentNullException(nameof(inner));

    private readonly ICompilerDiagnosticProvenanceBinder _provenance = provenance ?? throw new ArgumentNullException(nameof(provenance));

    public ReachabilityImportAnalysis Classify(ReachabilityImportRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        try
        {
            return _inner.Classify(request);
        }
        catch (CompilerException failure)
        {
            var enriched = _provenance.Bind(failure,
                new(request.Method.Definition.Key, request.Method.CanonicalName), request.DependencyPath);
            if (ReferenceEquals(enriched, failure)) throw;
            throw enriched;
        }
    }
}
