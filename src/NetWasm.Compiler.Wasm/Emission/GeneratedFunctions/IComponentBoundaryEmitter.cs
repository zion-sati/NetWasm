using System.Collections.Generic;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission.Planning;

namespace NetWasm.Compiler.Wasm.Emission.GeneratedFunctions;

internal interface IComponentBoundaryEmitter
{
    ComponentBoundaryArtifacts Emit(
        WasmEmissionRequest request,
        WasmTarget target,
        RuntimeInitializationPlan initialization,
        int importedFunctionCount,
        int definedFunctionCount,
        IReadOnlyDictionary<string, int> managedExportIndices,
        IFunctionIndexResolver functionIndices);
}
