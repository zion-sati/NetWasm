namespace NetWasm.Compiler.GarbageCollection;

public interface ICalleeArgumentRootingClassifier
{
    // The number of entries on top of the evaluation stack that a call hands
    // to a managed callee rooting them itself, or zero while the call site
    // must keep its arguments rooted.
    int Classify(RootDecisionRequest request);
}
