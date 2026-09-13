namespace NetWasm.Compiler.Analysis;

internal sealed class DelegateMethodClassifierFactory :
    IDelegateMethodClassifierFactory
{
    public IDelegateMethodClassifier Create(IDelegateTypeRecognizer delegateTypeRecognizer) =>
        new DelegateMethodClassifier(delegateTypeRecognizer);
}
