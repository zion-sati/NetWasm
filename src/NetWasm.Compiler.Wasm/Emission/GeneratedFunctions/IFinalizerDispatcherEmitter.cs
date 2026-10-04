using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission.Planning;

namespace NetWasm.Compiler.Wasm.Emission.GeneratedFunctions;

internal interface IFinalizerDispatcherEmitter
{
    byte[] Emit(
        FinalizerDispatchPlan[] finalizableTypes,
        IFunctionIndexResolver functionIndices);
}

internal sealed record FinalizerDispatchPlan(
    int TypeId,
    MethodInstanceModel Finalizer);
