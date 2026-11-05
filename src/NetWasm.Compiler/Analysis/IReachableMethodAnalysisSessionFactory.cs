namespace NetWasm.Compiler.Analysis;

/// <summary>Begins the method analysis one reachability closure uses.</summary>
internal interface IReachableMethodAnalysisSessionFactory
{
    ReachableMethodAnalysisSession Begin();
}
