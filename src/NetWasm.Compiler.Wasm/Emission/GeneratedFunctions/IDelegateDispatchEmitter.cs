using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Encoding;
using NetWasm.Compiler.Wasm.Emission.Planning;

namespace NetWasm.Compiler.Wasm.Emission.GeneratedFunctions;

// One single-cast delegate's call to its target: the delegate in DelegateLocal, the
// Invoke's arguments in their parameters, a scalar result left in ResultLocal. The
// method id local is declared only when the targets are more than are tested in turn.
internal sealed record DelegateDispatch(
    MethodInstanceModel Invoke,
    DelegateInvokeTarget Program,
    int DelegateLocal,
    int ResultLocal,
    int InitializationFrameLocal,
    int MethodIdLocal)
{
    // A few targets are tested in turn; more read the method id once.
    public const int SequentialTargets = 4;
}

internal interface IDelegateDispatchEmitter
{
    void Emit(IWasmInstructionWriter code, DelegateDispatch dispatch, IFunctionIndexResolver functionIndices);
}
