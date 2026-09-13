namespace NetWasm.Compiler.Analysis;

internal interface IReachableMethodAnalyzer
{
    ReachableMethodAnalysis Analyze(ReachableMethodRequest request);
}
