using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Analysis;

internal sealed class DelegateMethodClassifier(
    IDelegateTypeRecognizer delegateTypes) : IDelegateMethodClassifier
{
    public DelegateMethodKind Classify(MethodInstanceModel method)
    {
        if (!delegateTypes.Recognize(method.DeclaringType))
        {
            return DelegateMethodKind.None;
        }

        return method.Definition.Name switch
        {
            ".ctor" => DelegateMethodKind.Constructor,
            "Invoke" => DelegateMethodKind.Invoke,
            _ => DelegateMethodKind.None,
        };
    }
}
