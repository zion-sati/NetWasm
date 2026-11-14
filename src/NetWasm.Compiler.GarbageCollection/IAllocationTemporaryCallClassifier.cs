using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.GarbageCollection;

// A call that allocates an object and then runs managed code holding it only in the allocation temporary, as
// newobj does. Its constructor-call roots add that temporary, so the object survives a collection in the code.
public interface IAllocationTemporaryCallClassifier
{
    bool Classify(CilInstruction instruction);
}
