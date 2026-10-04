using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Metadata.UnsafeAccessors;

public interface IUnsafeAccessorFieldExposureClassifier
{
    bool Classify(MethodDefinitionModel method, FieldInstanceModel field);
}
