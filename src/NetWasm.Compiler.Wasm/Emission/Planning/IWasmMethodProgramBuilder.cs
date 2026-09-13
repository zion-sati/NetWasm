using System.Collections.Generic;
using NetWasm.Compiler.ControlFlow;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Wasm.Emission.Planning;

internal interface IWasmMethodProgramBuilder
{
    WasmMethodLoweringResult Build(
        IReadOnlyDictionary<EntityKey, ManagedMethodBody> methods,
        IReadOnlyDictionary<string, ManagedMethodBody> constructedMethods);
}
