using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission.Instructions.Runtime;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Tests;

// PreallocatedOutOfMemoryException.GetInstance returns the OutOfMemoryException
// frozen in static data, at the reference width.
public sealed class PreallocatedOutOfMemoryIntrinsicEmitterTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EmitPublishesTheFrozenException(bool memory64)
    {
        var layouts = new RecordingLayoutProvider();
        var request = EmitterTestSupport.CreateRuntimeIntrinsicRequest(
            RuntimeIntrinsic.PreallocatedOutOfMemoryException,
            []);
        request = request with { Target = memory64 ? WasmTargetLayout.Wasm64 : WasmTargetLayout.Wasm32 };
        var writer = new RecordingWriter();

        new PreallocatedOutOfMemoryIntrinsicEmitter(layouts).Emit(request, writer);

        Assert.Equal(
            [memory64 ? WasmOpcodes.I64Constant : WasmOpcodes.I32Constant, WasmOpcodes.LocalSet],
            writer.Instructions.Select(static instruction => instruction.Opcode));
        var address = layouts.GetExceptionObject(ManagedExceptionKind.OutOfMemory);
        Assert.Equal(address, memory64
            ? writer.Instructions[0].Operand!.Signed64Value
            : writer.Instructions[0].Operand!.SignedValue);
        Assert.Equal(
            (uint)request.Local(0, CliValueKind.ManagedReference),
            writer.Instructions[1].Operand!.UnsignedValue);
    }

    private sealed class RecordingWriter : IWasmInstructionWriter
    {
        public List<WasmInstruction> Instructions { get; } = [];

        public void Write(WasmInstruction instruction) => Instructions.Add(instruction);
    }
}
