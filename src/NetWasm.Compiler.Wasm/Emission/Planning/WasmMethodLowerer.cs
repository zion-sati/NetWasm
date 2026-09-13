using System;
using NetWasm.Compiler.ControlFlow;
using NetWasm.Compiler.ControlFlow.Structured;
using NetWasm.Compiler.ControlFlow.Structuring;

namespace NetWasm.Compiler.Wasm.Emission.Planning;

internal sealed class WasmMethodLowerer(
    IValidatedStructuredMethodBuilderFactory structuredMethods) : IWasmMethodLowerer
{
    private readonly IValidatedStructuredMethodBuilderFactory _structuredMethods =
        structuredMethods ?? throw new ArgumentNullException(nameof(structuredMethods));

    public StructuredMethod Lower(ManagedMethodBody method)
    {
        ArgumentNullException.ThrowIfNull(method);
        return _structuredMethods.Create().Build(method.ControlFlow);
    }
}
