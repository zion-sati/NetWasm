using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Wasm.Emission.GeneratedFunctions;

// An asynchronous export's completion observes its task with await semantics; a fault's
// exception goes to the capture import, whose payload the completion returns.
internal sealed record AsyncTaskCompletionPlan(
    int ObserveFunctionIndex,
    int CaptureFunctionIndex);

internal interface IAsyncTaskCompletionEmitter
{
    byte[] Emit(JavaScriptAsyncMethodBinding binding, AsyncTaskCompletionPlan plan);
}
