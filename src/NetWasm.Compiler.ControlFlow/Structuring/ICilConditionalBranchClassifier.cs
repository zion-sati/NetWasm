using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.ControlFlow.Structuring;

internal interface ICilConditionalBranchClassifier
{
    bool Classify(CilOperation operation);
}
