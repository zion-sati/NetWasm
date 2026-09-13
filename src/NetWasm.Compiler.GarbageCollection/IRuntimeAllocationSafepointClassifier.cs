using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.GarbageCollection;

public interface IRuntimeAllocationSafepointClassifier
{
    bool Classify(MethodDefinitionModel method, ITypeRepository types);

    bool Classify(MethodInstanceModel method);
}
