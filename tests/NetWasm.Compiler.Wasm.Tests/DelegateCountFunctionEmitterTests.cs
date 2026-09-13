using NetWasm.Compiler.Wasm.Emission.GeneratedFunctions;

namespace NetWasm.Compiler.Wasm.Tests;

using static EmitterTestSupport;

public sealed class DelegateCountFunctionEmitterTests
{
    [Fact]
    public void EmitsRecursiveDelegateCountFunction()
    {
        var layouts = new RecordingLayoutProvider();
        var emitter = new DelegateCountFunctionEmitter(
            layouts,
            layouts,
            CreateReferenceComparisons(layouts),
            new GeneratedFunctionWriterFactory());

        var bytes = ((IDelegateCountFunctionEmitter)emitter).Emit(
            CreateDelegateHelperTarget());

        Assert.NotEmpty(bytes);
        Assert.Contains(WasmOpcodes.Call, bytes);
        Assert.Contains(WasmOpcodes.I32Add, bytes);
    }
}
