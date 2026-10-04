using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Encoding;
using NetWasm.Compiler.Wasm.Emission;

namespace NetWasm.Compiler.Wasm.Tests;

using static EmitterTestSupport;

public sealed class ManagedTerminalTrapBoundaryEmitterTests
{
    [Theory]
    [InlineData(CliValueKind.Void, 0x40, 0x40)]
    [InlineData(CliValueKind.I4, 0x7f, 0x7f)]
    [InlineData(CliValueKind.I8, 0x7e, 0x7e)]
    [InlineData(CliValueKind.NativeInt, 0x7f, 0x7e)]
    [InlineData(CliValueKind.ManagedReference, 0x7f, 0x7e)]
    [InlineData(CliValueKind.ManagedAddress, 0x7f, 0x7e)]
    [InlineData(CliValueKind.F4, 0x7d, 0x7d)]
    [InlineData(CliValueKind.F8, 0x7c, 0x7c)]
    public void BoundaryPreservesTheTargetResultAndTrapsTheCaughtException(
        CliValueKind resultType,
        byte wasm32BlockType,
        byte wasm64BlockType)
    {
        foreach (var target in new[] { WasmTarget.Wasm32, WasmTarget.Wasm64 })
        {
            var layouts = new RecordingLayoutProvider(WasmTargetLayout.For(target));
            var emitter = new ManagedTerminalTrapBoundaryEmitter(
                layouts, new ExceptionPayloadBlockEmitter(layouts));
            var buffer = new WasmBinaryBuffer();
            var code = new WasmInstructionWriter(new WasmBinaryWriter(buffer));

            ((IManagedTerminalTrapBoundaryEmitter)emitter).Emit(code, resultType, resultLocal: 5,
                () => code.Write(WasmInstruction.NoOperand(0x01)));

            var body = new WasmBinarySnapshotReader(buffer).Read();
            Assert.Equal(WasmOpcodes.Block, body[0]);
            Assert.Equal(target == WasmTarget.Wasm32 ? wasm32BlockType : wasm64BlockType,
                body[1]);
            Assert.Equal(resultType != CliValueKind.Void,
                body.AsSpan().IndexOf(new byte[] { WasmOpcodes.LocalGet, 5 }) >= 0);
            Assert.True(body.AsSpan().IndexOf(new byte[]
            {
                0x01, WasmOpcodes.End,
            }) >= 0);
            Assert.True(body.AsSpan().EndsWith(new byte[]
            {
                WasmOpcodes.Branch, 1, WasmOpcodes.End,
                WasmOpcodes.Drop, WasmOpcodes.Unreachable, WasmOpcodes.End,
            }));
        }
    }

    [Theory]
    [InlineData(CliValueKind.ValueType)]
    [InlineData((CliValueKind)(-1))]
    public void InvalidResultFailsBeforeWritingOrInvokingTheBody(CliValueKind resultType)
    {
        var layouts = new RecordingLayoutProvider();
        var emitter = new ManagedTerminalTrapBoundaryEmitter(
            layouts, new ExceptionPayloadBlockEmitter(layouts));
        var buffer = new WasmBinaryBuffer();
        var code = new WasmInstructionWriter(new WasmBinaryWriter(buffer));
        var called = false;

        var exception = Assert.Throws<InvalidOperationException>(() =>
            ((IManagedTerminalTrapBoundaryEmitter)emitter).Emit(code, resultType, 0, () => called = true));

        Assert.Equal($"Terminal boundary cannot return {resultType}.", exception.Message);
        Assert.False(called);
        Assert.Empty(new WasmBinarySnapshotReader(buffer).Read());
    }
}
