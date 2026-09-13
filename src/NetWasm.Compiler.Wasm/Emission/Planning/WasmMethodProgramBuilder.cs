using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using NetWasm.Compiler.ControlFlow;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Wasm.Emission.Planning;

internal sealed class WasmMethodProgramBuilder(
    IWasmMethodLowerer methods) : IWasmMethodProgramBuilder
{
    private readonly IWasmMethodLowerer _methods = methods ??
        throw new ArgumentNullException(nameof(methods));

    public WasmMethodLoweringResult Build(
        IReadOnlyDictionary<EntityKey, ManagedMethodBody> methods,
        IReadOnlyDictionary<string, ManagedMethodBody> constructedMethods)
    {
        ArgumentNullException.ThrowIfNull(methods);
        ArgumentNullException.ThrowIfNull(constructedMethods);
        return new(
            methods.ToImmutableDictionary(
                pair => pair.Key,
                pair => _methods.Lower(pair.Value)),
            constructedMethods.ToImmutableDictionary(
                pair => pair.Key,
                pair => _methods.Lower(pair.Value),
                StringComparer.Ordinal));
    }
}
