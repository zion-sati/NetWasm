namespace NetWasm.Compiler.GarbageCollection;

public interface IRootDecisionClassifier
{
    bool Decide(RootDecisionRequest request);
}
