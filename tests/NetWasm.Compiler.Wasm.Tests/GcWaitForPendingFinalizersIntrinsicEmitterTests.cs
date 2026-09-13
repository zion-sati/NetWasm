using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission;
using NetWasm.Compiler.Wasm.Emission.Instructions.Runtime;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Tests;

public sealed class GcWaitForPendingFinalizersIntrinsicEmitterTests
{
    [Fact]
    public void EmitCallsRuntimeWaitCapability()
    {
        var runtimeImports = WasmRuntimeImports.CreateCatalog();
        var emitter = RuntimeIntrinsicEmitterTestFactory.Create(
            RuntimeIntrinsic.GcWaitForPendingFinalizers,
            runtimeImports);
        var request = EmitterTestSupport.CreateRuntimeIntrinsicRequest(
            RuntimeIntrinsic.GcWaitForPendingFinalizers,
            []);
        var writer = new RecordingWriter();

        emitter.Emit(request, writer);

        var instruction = Assert.Single(writer.Instructions);
        Assert.Equal(WasmOpcodes.Call, instruction.Opcode);
        Assert.Equal(
            (uint)runtimeImports.Resolve(RuntimeImportSymbol.GcWaitForPendingFinalizers),
            instruction.Operand!.UnsignedValue);
    }

    private sealed class RecordingWriter : IWasmInstructionWriter
    {
        private readonly List<WasmInstruction> _instructions = [];

        public List<WasmInstruction> Instructions => _instructions;

        public void Write(WasmInstruction instruction) => _instructions.Add(instruction);
    }
}
