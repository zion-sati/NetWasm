using NetWasm.Compiler.ControlFlow.Structured;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Wasm.Emission.Methods;

internal interface IFilterEnvironmentLayoutPlanner
{
    FilterEnvironmentLayout Create(
        StructuredMethod method,
        ValueFrameLayout valueLayout);
}
