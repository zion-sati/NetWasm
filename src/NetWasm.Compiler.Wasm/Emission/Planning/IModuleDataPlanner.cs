using System.Collections.Generic;
using NetWasm.Compiler.ControlFlow.Structured;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Wasm.Emission.Planning;

internal interface IModuleDataPlanner
{
    ModuleDataPlan Build(
        IEnumerable<StructuredMethodEmission> methods,
        IReadOnlyList<EntityKey> directInitializers,
        IReadOnlyList<string> constructedInitializers);
}
