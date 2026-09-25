using System.Collections.Generic;
using NetWasm.Compiler.Wasm.Emission.Planning;

namespace NetWasm.Compiler.Wasm.Emission.GeneratedFunctions;

internal interface IStaticInitializerFunctionAppender
{
    void Append(IList<WasmFunctionDefinition> functions, StaticInitializerFunctionPlan? plan);
}
