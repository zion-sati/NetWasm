using System.Collections.Immutable;
using NetWasm.Compiler.ControlFlow.Structured;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Encoding;
using NetWasm.Compiler.Wasm.Emission;

namespace NetWasm.Compiler.Wasm.Tests;

using static NetWasm.Compiler.Wasm.Tests.EmitterTestSupport;

public sealed class ImplicitExceptionEmitterTests
{
    [Fact]
    public void ExceptionPayloadBlockUsesManagedReferenceWidth()
    {
        var wasm32Buffer = new WasmBinaryBuffer();
        var wasm64Buffer = new WasmBinaryBuffer();
        var wasm32Output = new WasmBinaryWriter(wasm32Buffer);
        var wasm64Output = new WasmBinaryWriter(wasm64Buffer);
        var wasm32 = new WasmInstructionWriter(wasm32Output);
        var wasm64 = new WasmInstructionWriter(wasm64Output);

        new ExceptionPayloadBlockEmitter(new RecordingLayoutProvider()).Emit(wasm32);
        new ExceptionPayloadBlockEmitter(
            new RecordingLayoutProvider(WasmTargetLayout.Wasm64)).Emit(wasm64);

        Assert.Equal(
            [WasmOpcodes.Block, (byte)WasmValueType.I32],
            new WasmBinarySnapshotReader(wasm32Buffer).Read());
        Assert.Equal(
            [WasmOpcodes.Block, (byte)WasmValueType.I64],
            new WasmBinarySnapshotReader(wasm64Buffer).Read());
    }

    [Fact]
    public void LoweringEmitsCurrentWasmEhForRoslynFinallyShape()
    {
        var program = new FakeProgram();
        var entry = program.GetMethod(EntryKey);
        var body = new CilMethodBody(
            entry,
            1,
            [],
            [
                I(0, CilOperation.Nop),
                I(1, CilOperation.Leave, new CilOperand.BranchTarget(4)),
                I(2, CilOperation.Nop),
                I(3, CilOperation.EndFinally),
                I(4, CilOperation.LoadInt32, new CilOperand.ConstantI4(7)),
                I(5, CilOperation.Return),
            ])
        {
            ExceptionRegions =
            [
                new CilExceptionRegion(
                    CilExceptionRegionKind.Finally,
                    0,
                    2,
                    2,
                    2,
                    null,
                    null),
            ],
        };
        var structured = Structure(program, body);

        var module = CreateManagedWasmEmitter(
            program,
            new FakeIntrinsics(),
            new RecordingLayoutProvider(),
            new WasmModuleEmitterFactory()).Emit(
                entry,
                new Dictionary<EntityKey, StructuredMethod> { [EntryKey] = structured },
                EmptyRootMaps([EntryKey]),
                [],
                ImmutableDictionary<string, EntityKey>.Empty);

        byte[] tryTable = [0x1f, 0x40, 0x01, 0x00];
        byte[] rethrow = [0x08, 0x00];
        Assert.True(module.AsSpan().IndexOf(tryTable) >= 0);
        Assert.True(module.AsSpan().IndexOf(rethrow) >= 0);
    }

}
