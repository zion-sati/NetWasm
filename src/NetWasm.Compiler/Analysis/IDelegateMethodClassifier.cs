using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Analysis;

internal interface IDelegateMethodClassifier
{
    DelegateMethodKind Classify(MethodInstanceModel method);
}
