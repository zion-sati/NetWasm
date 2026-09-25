using NetWasm.Compiler.Wasm.Emission.Planning;

namespace NetWasm.Compiler.Wasm.Emission.GeneratedFunctions;

internal interface IStaticInitializerFunctionEmitter
{
    byte[] Emit(StaticInitializerFunction initializer, StaticInitializerFunctionPlan plan);
}
