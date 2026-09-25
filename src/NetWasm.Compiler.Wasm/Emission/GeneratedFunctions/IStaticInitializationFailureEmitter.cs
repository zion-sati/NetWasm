using NetWasm.Compiler.Wasm.Emission.Planning;

namespace NetWasm.Compiler.Wasm.Emission.GeneratedFunctions;

internal interface IStaticInitializationFailureEmitter
{
    byte[] Emit(StaticInitializerFunctionPlan plan);
}
