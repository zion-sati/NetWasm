namespace NetWasm.Compiler.Analysis;

internal interface IReachabilityImportClassifier
{
    ReachabilityImportAnalysis Classify(ReachabilityImportRequest request);
}
