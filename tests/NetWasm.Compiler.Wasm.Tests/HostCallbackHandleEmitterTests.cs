using NetWasm.Compiler.Wasm.Emission;
using NetWasm.Compiler.Wasm.Emission.Instructions.Interop;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Tests;

using static EmitterTestSupport;

public sealed class HostCallbackHandleEmitterTests
{
    [Fact]
    public void CreatesAHostHandleAndLeavesItOnTheEvaluationStack()
    {
        var imports = WasmRuntimeImports.CreateCatalog();
        var code = new RecordingInstructionWriter();
        var emitter = ThroughContract(new HostCallbackHandleEmitter(imports));

        emitter.Emit(code, 4, CreateMethodEmissionContext());

        byte[] expected =
        [
            WasmOpcodes.LocalGet, 4,
            WasmOpcodes.Call, (byte)imports.Resolve(RuntimeImportSymbol.HandleNew),
            WasmOpcodes.LocalSet, 24,
            WasmOpcodes.LocalGet, 24,
        ];
        Assert.Equal(expected, code.ToArray());
    }

    private static IHostCallbackHandleEmitter ThroughContract(
        HostCallbackHandleEmitter emitter) => new[]
        {
            emitter,
        }.Cast<IHostCallbackHandleEmitter>().Single();
}
