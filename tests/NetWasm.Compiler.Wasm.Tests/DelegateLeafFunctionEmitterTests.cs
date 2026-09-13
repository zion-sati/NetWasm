using NetWasm.Compiler.Wasm.Emission.GeneratedFunctions;

namespace NetWasm.Compiler.Wasm.Tests;

using static EmitterTestSupport;

public sealed class DelegateLeafFunctionEmitterTests
{
    [Fact]
    public void EmitsIndexedDelegateLeafTraversal()
    {
        var layouts = new RecordingLayoutProvider();
        var emitter = new DelegateLeafFunctionEmitter(
            layouts,
            layouts,
            CreateReferenceComparisons(layouts),
            CreateAddressInstructions(layouts),
            new GeneratedFunctionWriterFactory());

        var bytes = ((IDelegateLeafFunctionEmitter)emitter).Emit(
            CreateDelegateHelperTarget());

        Assert.NotEmpty(bytes);
        Assert.Contains(WasmOpcodes.Call, bytes);
        Assert.Contains(WasmOpcodes.I32Subtract, bytes);
    }
}
