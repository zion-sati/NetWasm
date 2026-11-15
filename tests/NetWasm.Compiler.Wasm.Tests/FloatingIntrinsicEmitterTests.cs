// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission.Instructions.Runtime;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Tests;

public sealed class FloatingIntrinsicEmitterTests
{
    [Theory]
    [InlineData(RuntimeIntrinsic.SingleToInt32Bits, CliValueKind.F4, CliValueKind.I4, WasmOpcodes.I32ReinterpretF32)]
    [InlineData(RuntimeIntrinsic.Int32BitsToSingle, CliValueKind.I4, CliValueKind.F4, WasmOpcodes.F32ReinterpretI32)]
    [InlineData(RuntimeIntrinsic.DoubleToInt64Bits, CliValueKind.F8, CliValueKind.I8, WasmOpcodes.I64ReinterpretF64)]
    [InlineData(RuntimeIntrinsic.Int64BitsToDouble, CliValueKind.I8, CliValueKind.F8, WasmOpcodes.F64ReinterpretI64)]
    [InlineData(RuntimeIntrinsic.FloatingAbsolute, CliValueKind.F4, CliValueKind.F4, WasmOpcodes.F32Absolute)]
    [InlineData(RuntimeIntrinsic.FloatingAbsolute, CliValueKind.F8, CliValueKind.F8, WasmOpcodes.F64Absolute)]
    [InlineData(RuntimeIntrinsic.FloatingCeiling, CliValueKind.F4, CliValueKind.F4, WasmOpcodes.F32Ceiling)]
    [InlineData(RuntimeIntrinsic.FloatingCeiling, CliValueKind.F8, CliValueKind.F8, WasmOpcodes.F64Ceiling)]
    [InlineData(RuntimeIntrinsic.FloatingFloor, CliValueKind.F4, CliValueKind.F4, WasmOpcodes.F32Floor)]
    [InlineData(RuntimeIntrinsic.FloatingFloor, CliValueKind.F8, CliValueKind.F8, WasmOpcodes.F64Floor)]
    [InlineData(RuntimeIntrinsic.FloatingTruncate, CliValueKind.F4, CliValueKind.F4, WasmOpcodes.F32Truncate)]
    [InlineData(RuntimeIntrinsic.FloatingTruncate, CliValueKind.F8, CliValueKind.F8, WasmOpcodes.F64Truncate)]
    [InlineData(RuntimeIntrinsic.FloatingRound, CliValueKind.F4, CliValueKind.F4, WasmOpcodes.F32Nearest)]
    [InlineData(RuntimeIntrinsic.FloatingRound, CliValueKind.F8, CliValueKind.F8, WasmOpcodes.F64Nearest)]
    [InlineData(RuntimeIntrinsic.FloatingSquareRoot, CliValueKind.F4, CliValueKind.F4, WasmOpcodes.F32SquareRoot)]
    [InlineData(RuntimeIntrinsic.FloatingSquareRoot, CliValueKind.F8, CliValueKind.F8, WasmOpcodes.F64SquareRoot)]
    public void EmitsEveryFloatingIntrinsicThroughItsCapability(
        RuntimeIntrinsic intrinsic,
        CliValueKind argument,
        CliValueKind result,
        byte expectedOpcode)
    {
        var emitter = CreateEmitter(intrinsic);
        foreach (var target in new[] { WasmTarget.Wasm32, WasmTarget.Wasm64 })
        {
            var request = EmitterTestSupport.CreateRuntimeIntrinsicRequest(intrinsic, [argument]) with
            {
                Target = WasmTargetLayout.For(target),
            };
            var writer = new EmitterTestSupport.RecordingInstructionWriter();

            emitter.Emit(request, writer);

            Assert.Equal(
                new[]
                {
                    WasmInstruction.WithOperand(WasmOpcodes.LocalGet,
                        WasmInstructionOperand.Unsigned((uint)request.Local(0, argument))),
                    WasmInstruction.NoOperand(expectedOpcode),
                    WasmInstruction.WithOperand(WasmOpcodes.LocalSet,
                        WasmInstructionOperand.Unsigned((uint)request.Local(0, result))),
                },
                writer.ToInstructions());
        }
    }

    [Fact]
    public void RejectsNonFloatingUnaryArgument()
    {
        var emitter = new FloatingAbsoluteIntrinsicEmitter();
        var request = EmitterTestSupport.CreateRuntimeIntrinsicRequest(
            RuntimeIntrinsic.FloatingAbsolute,
            [CliValueKind.I4]);

        Assert.Throws<InvalidOperationException>(() => EmitThroughCapability(emitter, request));
    }

    [Theory]
    [InlineData(RuntimeIntrinsic.FloatingCeiling)]
    [InlineData(RuntimeIntrinsic.FloatingFloor)]
    [InlineData(RuntimeIntrinsic.FloatingTruncate)]
    [InlineData(RuntimeIntrinsic.FloatingRound)]
    [InlineData(RuntimeIntrinsic.FloatingSquareRoot)]
    public void RejectsNonFloatingArgumentForEveryUnaryFloatingCapability(
        RuntimeIntrinsic intrinsic)
    {
        var emitter = CreateEmitter(intrinsic);
        var request = EmitterTestSupport.CreateRuntimeIntrinsicRequest(intrinsic, [CliValueKind.I4]);

        Assert.Throws<InvalidOperationException>(() => EmitThroughCapability(emitter, request));
    }

    private static IRuntimeIntrinsicEmitter CreateEmitter(RuntimeIntrinsic intrinsic) =>
        intrinsic switch
        {
            RuntimeIntrinsic.SingleToInt32Bits => new SingleToInt32BitsIntrinsicEmitter(),
            RuntimeIntrinsic.Int32BitsToSingle => new Int32BitsToSingleIntrinsicEmitter(),
            RuntimeIntrinsic.DoubleToInt64Bits => new DoubleToInt64BitsIntrinsicEmitter(),
            RuntimeIntrinsic.Int64BitsToDouble => new Int64BitsToDoubleIntrinsicEmitter(),
            RuntimeIntrinsic.FloatingAbsolute => new FloatingAbsoluteIntrinsicEmitter(),
            RuntimeIntrinsic.FloatingCeiling => new FloatingCeilingIntrinsicEmitter(),
            RuntimeIntrinsic.FloatingFloor => new FloatingFloorIntrinsicEmitter(),
            RuntimeIntrinsic.FloatingTruncate => new FloatingTruncateIntrinsicEmitter(),
            RuntimeIntrinsic.FloatingRound => new FloatingRoundIntrinsicEmitter(),
            RuntimeIntrinsic.FloatingSquareRoot => new FloatingSquareRootIntrinsicEmitter(),
            _ => throw new ArgumentOutOfRangeException(nameof(intrinsic)),
        };

    private static void EmitThroughCapability(
        IRuntimeIntrinsicEmitter emitter,
        RuntimeIntrinsicEmissionRequest request) => emitter.Emit(
            request,
            EmitterTestSupport.GetCodeWriter(request.Instruction));
}
