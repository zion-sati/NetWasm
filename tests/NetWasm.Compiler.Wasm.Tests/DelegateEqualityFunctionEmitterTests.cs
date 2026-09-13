using NetWasm.Compiler.Wasm.Emission.GeneratedFunctions;

namespace NetWasm.Compiler.Wasm.Tests;

using static EmitterTestSupport;

public sealed class DelegateEqualityFunctionEmitterTests
{
    [Fact]
    public void EmitsOrderedInvocationListEquality()
    {
        var layouts = new RecordingLayoutProvider();
        var emitter = new DelegateEqualityFunctionEmitter(
            layouts,
            CreateReferenceComparisons(layouts),
            CreateDelegateLeafEquality(layouts),
            new GeneratedFunctionWriterFactory());

        var bytes = ((IDelegateEqualityFunctionEmitter)emitter).Emit(
            CreateDelegateHelperTarget());

        Assert.NotEmpty(bytes);
        Assert.Contains(WasmOpcodes.Loop, bytes);
        Assert.Contains(WasmOpcodes.I32Equal, bytes);
    }
}
