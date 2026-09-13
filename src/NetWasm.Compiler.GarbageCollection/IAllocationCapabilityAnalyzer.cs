namespace NetWasm.Compiler.GarbageCollection;

public interface IAllocationCapabilityAnalyzer
{
    AllocationCapabilities Analyze(AllocationCapabilityAnalysisRequest request);
}
