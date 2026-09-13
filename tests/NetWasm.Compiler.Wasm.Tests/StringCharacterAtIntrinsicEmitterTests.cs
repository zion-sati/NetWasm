using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission;
using NetWasm.Compiler.Wasm.Emission.Instructions.Runtime;
using NetWasm.Compiler.Wasm.Emission.Support;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Tests;

using static EmitterTestSupport;

public sealed class StringCharacterAtIntrinsicEmitterTests
{
    [Theory]
    [InlineData(WasmTarget.Wasm32, WasmOpcodes.I32Multiply, WasmOpcodes.I32Add)]
    [InlineData(WasmTarget.Wasm64, WasmOpcodes.I64Multiply, WasmOpcodes.I64Add)]
    public void ChecksBoundsAndLoadsACharacterForEachMemoryWidth(
        WasmTarget target,
        byte multiplyOpcode,
        byte addOpcode)
    {
        var layout = WasmTargetLayout.For(target);
        var layouts = new RecordingLayoutProvider(layout);
        var request = CreateRuntimeIntrinsicRequest(
            RuntimeIntrinsic.StringCharacterAt,
            [CliValueKind.ManagedReference, CliValueKind.I4]) with
        {
            Target = layout,
        };
        var code = new RecordingInstructionWriter();
        var emitter = ThroughContract(new StringCharacterAtIntrinsicEmitter(
            layouts,
            layouts,
            new ImplicitExceptionEmitter(layouts, layouts, 7),
            CreateAddressInstructions(layouts)));

        emitter.Emit(request, code);

        var bytes = code.ToArray();
        Assert.Contains(WasmOpcodes.I32LessThanSigned, bytes);
        Assert.Contains(WasmOpcodes.I32GreaterThanOrEqualUnsigned, bytes);
        Assert.Contains(multiplyOpcode, bytes);
        Assert.Contains(addOpcode, bytes);
        Assert.Contains(WasmOpcodes.I32Load16Unsigned, bytes);
        Assert.Contains(WasmOpcodes.Throw, bytes);
    }

    private static IRuntimeIntrinsicEmitter ThroughContract(
        StringCharacterAtIntrinsicEmitter emitter) => new[]
        {
            emitter,
        }.Cast<IRuntimeIntrinsicEmitter>().Single();
}
