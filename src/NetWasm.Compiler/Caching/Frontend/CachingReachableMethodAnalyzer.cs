using NetWasm.Compiler.Analysis;
using System;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Metadata;

namespace NetWasm.Compiler.Caching.Frontend;

internal sealed class CachingReachableMethodAnalyzer(
    IReachableMethodAnalyzer inner,
    IFrontendArtifactRestorer restorer,
    IFrontendAnalysisRecorder recorder) : IReachableMethodAnalyzer
{
    private readonly IReachableMethodAnalyzer _inner = inner ??
        throw new ArgumentNullException(nameof(inner));
    private readonly IFrontendArtifactRestorer _restorer = restorer ??
        throw new ArgumentNullException(nameof(restorer));
    private readonly IFrontendAnalysisRecorder _recorder = recorder ??
        throw new ArgumentNullException(nameof(recorder));

    public ReachableMethodAnalysis Analyze(ReachableMethodRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (_restorer.TryRestore(request.Method, out var artifact))
        {
            return artifact.Analysis;
        }
        var analysis = _inner.Analyze(request);
        _recorder.Record(analysis);
        return analysis;
    }
}

internal sealed class CachingReachableMethodAnalyzerFactory(
    IReachableMethodAnalyzerFactory inner,
    IFrontendArtifactRestorer restorer,
    IFrontendAnalysisRecorder recorder) : IReachableMethodAnalyzerFactory
{
    private readonly IReachableMethodAnalyzerFactory _inner = inner ??
        throw new ArgumentNullException(nameof(inner));
    private readonly IFrontendArtifactRestorer _restorer = restorer ??
        throw new ArgumentNullException(nameof(restorer));
    private readonly IFrontendAnalysisRecorder _recorder = recorder ??
        throw new ArgumentNullException(nameof(recorder));

    public IReachableMethodAnalyzer Create(
        IMethodBodyReader methodBodies,
        ITypeRepository types,
        IFieldRepository fields,
        IMethodRepository methods,
        IMethodSpecializer specializer,
        IImplicitExceptionDiscovery exceptionDiscovery,
        IReachabilityInstructionAnalyzer instructionAnalyzer) =>
        new CachingReachableMethodAnalyzer(
            _inner.Create(
                methodBodies,
                types,
                fields,
                methods,
                specializer,
                exceptionDiscovery,
                instructionAnalyzer),
            _restorer,
            _recorder);
}
