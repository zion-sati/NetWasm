using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission.Instructions.Runtime;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Tests;

public sealed class StringSetCharacterUncheckedIntrinsicEmitterTests
{
    [Fact]
    public void UsesMemory64AddressArithmeticForUncheckedCharacterMutation()
    {
        var layouts = new RecordingLayoutProvider(WasmTargetLayout.Wasm64);
        var emitter = new StringSetCharacterUncheckedIntrinsicEmitter(
            layouts,
            layouts);
        var request = EmitterTestSupport.CreateRuntimeIntrinsicRequest(
            RuntimeIntrinsic.StringSetCharacterUnchecked,
            [CliValueKind.ManagedReference, CliValueKind.I4, CliValueKind.I4]) with
        {
            Target = WasmTargetLayout.Wasm64,
        };

        emitter.Emit(
            request,
            EmitterTestSupport.GetCodeWriter(request.Instruction));

        var body = EmitterTestSupport.GetCodeBytes(request.Instruction);
        Assert.Contains(WasmOpcodes.I64ExtendI32Unsigned, body);
        Assert.Contains(WasmOpcodes.I64Constant, body);
        Assert.Contains(WasmOpcodes.I64Multiply, body);
        Assert.Contains(WasmOpcodes.I64Add, body);
        Assert.Contains(WasmOpcodes.I32Store16, body);
    }
}
