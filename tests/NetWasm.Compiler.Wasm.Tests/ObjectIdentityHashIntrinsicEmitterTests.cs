using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Tests;

public sealed class ObjectIdentityHashIntrinsicEmitterTests
{
    [Fact]
    public void EmitHashesReferenceAndPublishesInt32Result()
    {
        var runtimeImports = WasmRuntimeImports.CreateCatalog();
        var emitter = RuntimeIntrinsicEmitterTestFactory.Create(
            RuntimeIntrinsic.ObjectIdentityHash,
            runtimeImports);
        var request = EmitterTestSupport.CreateRuntimeIntrinsicRequest(
            RuntimeIntrinsic.ObjectIdentityHash,
            [CliValueKind.ManagedReference]);
        var writer = new RecordingWriter();

        emitter.Emit(request, writer);

        Assert.Equal(
            [WasmOpcodes.LocalGet, WasmOpcodes.Call, WasmOpcodes.LocalSet],
            writer.Instructions.Select(instruction => instruction.Opcode));
        Assert.Equal(
            (uint)request.Local(0, CliValueKind.ManagedReference),
            writer.Instructions[0].Operand!.UnsignedValue);
        Assert.Equal(
            (uint)runtimeImports.Resolve(RuntimeImportSymbol.ObjectIdentityHash),
            writer.Instructions[1].Operand!.UnsignedValue);
        Assert.Equal(
            (uint)request.Local(0, CliValueKind.I4),
            writer.Instructions[2].Operand!.UnsignedValue);
    }

    private sealed class RecordingWriter : IWasmInstructionWriter
    {
        private readonly List<WasmInstruction> _instructions = [];

        public List<WasmInstruction> Instructions => _instructions;

        public void Write(WasmInstruction instruction) => _instructions.Add(instruction);
    }
}
