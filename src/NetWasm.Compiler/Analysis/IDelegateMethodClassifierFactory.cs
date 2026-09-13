namespace NetWasm.Compiler.Analysis;

internal interface IDelegateMethodClassifierFactory
{
    IDelegateMethodClassifier Create(IDelegateTypeRecognizer delegateTypes);
}
