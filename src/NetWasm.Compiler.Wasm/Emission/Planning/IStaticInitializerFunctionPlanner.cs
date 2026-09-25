using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Wasm.Emission.Planning;

internal interface IStaticInitializerFunctionPlanner
{
    ModuleDataPlan Build(WasmEmissionRequest request, WasmModulePlan functions, ModuleDataPlan data);
}
