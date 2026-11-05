using System.Collections.Immutable;
using NetWasm.Compiler.Analysis;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission;

namespace NetWasm.Compiler.Tests;

public sealed class CompilerConcurrencyPolicyTests
{
    [Fact]
    public void AnalysisConcurrencyKeepsBrowserSynchronousAndUsesNativeProcessors()
    {
        Assert.Equal(new AnalysisConcurrency(1, true),
            WholeProgramAnalyzerFactory.SelectConcurrency(true, 32));
        Assert.Equal(new AnalysisConcurrency(8, false),
            WholeProgramAnalyzerFactory.SelectConcurrency(false, 8));
        Assert.Equal(1, WholeProgramAnalyzerFactory.SelectConcurrency(true, 32).AnalyzerCount);
        Assert.Equal(9, WholeProgramAnalyzerFactory.SelectConcurrency(false, 8).AnalyzerCount);

        var one = ImmutableArray.Create<IReachableMethodAnalyzer>(new UnusedAnalyzer());
        var several = ImmutableArray.Create<IReachableMethodAnalyzer>(new UnusedAnalyzer(), new UnusedAnalyzer());
        var observer = new RecordingReachableMethodBatchObserver();
        Assert.IsType<SynchronousReachableMethodAnalysisSessionFactory>(
            WholeProgramAnalyzerFactory.CreateMethodAnalysisSessions(true, several, observer));
        Assert.IsType<SynchronousReachableMethodAnalysisSessionFactory>(
            WholeProgramAnalyzerFactory.CreateMethodAnalysisSessions(false, one, observer));
        Assert.IsType<PooledReachableMethodAnalysisSessionFactory>(
            WholeProgramAnalyzerFactory.CreateMethodAnalysisSessions(false, several, observer));
    }

    [Fact]
    public void WasmEmissionWorkerSelectionHonorsTheHostBoundary()
    {
        Assert.Equal(1, WasmModuleEmitterFactory.SelectWorkerCount(
            true, new CompilerParallelism(4)));
        Assert.Equal(4, WasmModuleEmitterFactory.SelectWorkerCount(
            false, new CompilerParallelism(4)));
        Assert.Equal(1, WasmModuleEmitterFactory.SelectWorkerCount(false, null));
    }

    private sealed class UnusedAnalyzer : IReachableMethodAnalyzer
    {
        public ReachableMethodAnalysis Analyze(ReachableMethodRequest request) =>
            throw new NotSupportedException();
    }
}
