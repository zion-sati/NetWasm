namespace NetWasm.Compiler.Analysis;

internal interface IReachabilityInstructionAnalyzer
{
    ReachabilityInstructionAnalysis Analyze(ReachabilityInstructionRequest request);
}
