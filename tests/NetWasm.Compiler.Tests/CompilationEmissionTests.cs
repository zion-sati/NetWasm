using System.Collections.Immutable;
using NetWasm.Compiler.ControlFlow.Structured;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Pipeline;
using NetWasm.Compiler.Wasm.Emission;
using NetWasm.Compiler.Wasm.Emission.Planning;

namespace NetWasm.Compiler.Tests;

public sealed class CompilationEmissionTests
{
    [Fact]
    public void RetainsTheEmissionAndItsLoweredMethods()
    {
        var result = new WasmModuleEmissionResult([], 0);
        var lowering = new WasmMethodLoweringResult(
            ImmutableDictionary<EntityKey, StructuredMethod>.Empty,
            ImmutableDictionary<string, StructuredMethod>.Empty);

        var types = new NetWasm.Compiler.Symbols.RuntimeTypeSymbolTable([], [], []);
        var emission = new CompilationEmission(result, lowering, types);

        Assert.Same(result, emission.Result);
        Assert.Same(lowering, emission.Lowering);
        Assert.Same(types, emission.TypeSymbols);
    }

    [Fact]
    public void RequiresBothResults()
    {
        var result = new WasmModuleEmissionResult([], 0);
        var lowering = new WasmMethodLoweringResult(
            ImmutableDictionary<EntityKey, StructuredMethod>.Empty,
            ImmutableDictionary<string, StructuredMethod>.Empty);

        var types = new NetWasm.Compiler.Symbols.RuntimeTypeSymbolTable([], [], []);
        Assert.Throws<ArgumentNullException>(() => new CompilationEmission(null!, lowering, types));
        Assert.Throws<ArgumentNullException>(() => new CompilationEmission(result, null!, types));
        Assert.Throws<ArgumentNullException>(() => new CompilationEmission(result, lowering, null!));
    }
}
