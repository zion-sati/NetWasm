using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Analysis.Attributes;

internal interface IAttributeQueryClassifier
{
    AttributeQueryCall? Classify(MethodInstanceModel method);
}
