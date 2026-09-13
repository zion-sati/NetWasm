using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Encoding;
using NetWasm.Compiler.Wasm.Emission;
using NetWasm.Compiler.Wasm.Emission.Support;

namespace NetWasm.Compiler.Wasm.Tests;

public sealed class AddressInstructionEmitterTests
{
    [Theory]
    [InlineData(false, WasmOpcodes.I32Constant, WasmOpcodes.I32Add, WasmOpcodes.I32EqualZero)]
    [InlineData(true, WasmOpcodes.I64Constant, WasmOpcodes.I64Add, WasmOpcodes.I64EqualZero)]
    public void EmitsWidthSpecificAddressOperations(
        bool memory64,
        byte constant,
        byte add,
        byte equalZero)
    {
        var target = memory64 ? WasmTargetLayout.Wasm64 : WasmTargetLayout.Wasm32;
        var outputBuffer = new WasmBinaryBuffer();
        var output = new WasmBinaryWriter(outputBuffer);
        var code = new WasmInstructionWriter(output);
        var emitter = EmitterTestSupport.CreateAddressInstructions(
            new RecordingLayoutProvider(target));

        emitter.Emit(code, 7);
        emitter.Emit(code, AddressOperation.Add);
        emitter.Emit(code, AddressOperation.EqualZero);

        var bytes = new WasmBinarySnapshotReader(outputBuffer).Read();
        Assert.Contains(constant, bytes);
        Assert.Contains(add, bytes);
        Assert.Contains(equalZero, bytes);
    }

    [Fact]
    public void RejectsUnknownAddressOperationThroughItsContract()
    {
        var emitter = EmitterTestSupport.CreateAddressInstructions(
            new RecordingLayoutProvider());

        var exception = Assert.Throws<ArgumentOutOfRangeException>(() =>
            emitter.Emit(
                new EmitterTestSupport.RecordingInstructionWriter(),
                (AddressOperation)(-1)));

        Assert.Equal("operation", exception.ParamName);
    }
}
