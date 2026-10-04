using NetWasm.Compiler.Wasm.Emission.Planning;

namespace NetWasm.Compiler.Wasm.Emission.GeneratedFunctions;

internal interface INativeCallbackThunkEmitter
{
    byte[] Emit(
        NativeCallbackMethodPlan callback,
        ModuleDataPlan moduleData,
        RuntimeImportSelection runtimeImportSelection,
        IFunctionIndexResolver functionIndices);
}
