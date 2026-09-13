using System.Collections.Immutable;
using System.Collections.Generic;

namespace NetWasm.Compiler.Analysis;

internal interface IReachableMethodBatchAnalyzer
{
    IEnumerable<ReachableMethodAnalysis> Analyze(
        ImmutableArray<ReachableMethodRequest> requests);
}
