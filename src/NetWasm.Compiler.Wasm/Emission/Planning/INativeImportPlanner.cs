using System.Collections.Generic;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Wasm.Emission.Planning;

internal interface INativeImportPlanner
{
    NativeImportPlan Plan(IEnumerable<MethodInstanceModel> methods);
}
