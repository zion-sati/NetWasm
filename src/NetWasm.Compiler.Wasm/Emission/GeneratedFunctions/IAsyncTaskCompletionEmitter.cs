using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Wasm.Emission.GeneratedFunctions;

internal sealed record AsyncTaskCompletionPlan(
    int ObserveFunctionIndex,
    int? DeliverFunctionIndex,
    CliValueKind ResultKind);

internal interface IAsyncTaskCompletionEmitter
{
    byte[] Emit(JavaScriptAsyncMethodBinding binding, AsyncTaskCompletionPlan plan);
}
