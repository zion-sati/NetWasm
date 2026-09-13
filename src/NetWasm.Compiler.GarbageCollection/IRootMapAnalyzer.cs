using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.GarbageCollection;

public interface IRootMapAnalyzer
{
    MethodRootMap Analyze(RootMapAnalysisRequest request);
}
