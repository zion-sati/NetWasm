// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission.Instructions.Runtime;

namespace NetWasm.Compiler.Wasm.Tests;

public sealed class IntegerBitOperationIntrinsicEmitterTests
{
    [Fact]
    public void RejectsAnUnsupportedIntrinsicBeforeWritingInstructions()
    {
        var request = EmitterTestSupport.CreateRuntimeIntrinsicRequest(
            RuntimeIntrinsic.IntegerDivRem, [CliValueKind.I4]);

        Assert.Throws<InvalidOperationException>(() => Emit(request));

        Assert.Empty(EmitterTestSupport.GetCodeBytes(request.Instruction));
    }

    [Theory]
    [InlineData(RuntimeIntrinsic.IntegerRotateLeft, false)]
    [InlineData(RuntimeIntrinsic.IntegerRotateLeft, true)]
    [InlineData(RuntimeIntrinsic.IntegerRotateRight, false)]
    [InlineData(RuntimeIntrinsic.IntegerRotateRight, true)]
    public void RejectsRotateArityAndOffsetTypeBeforeWritingInstructions(RuntimeIntrinsic intrinsic, bool wrongType)
    {
        var request = EmitterTestSupport.CreateRuntimeIntrinsicRequest(intrinsic,
            wrongType ? [CliValueKind.I4, CliValueKind.I8] : [CliValueKind.I4]);

        Assert.Throws<InvalidOperationException>(() => Emit(request));

        Assert.Empty(EmitterTestSupport.GetCodeBytes(request.Instruction));
    }

    [Theory]
    [InlineData(RuntimeIntrinsic.IntegerLeadingZeroCount, CliValueKind.I4,
        WasmOpcodes.I32CountLeadingZeros)]
    [InlineData(RuntimeIntrinsic.IntegerLeadingZeroCount, CliValueKind.I8,
        WasmOpcodes.I64CountLeadingZeros)]
    [InlineData(RuntimeIntrinsic.IntegerLog2, CliValueKind.I4,
        WasmOpcodes.I32CountLeadingZeros)]
    [InlineData(RuntimeIntrinsic.IntegerLog2, CliValueKind.I8,
        WasmOpcodes.I64CountLeadingZeros)]
    [InlineData(RuntimeIntrinsic.IntegerPopulationCount, CliValueKind.I4,
        WasmOpcodes.I32PopulationCount)]
    [InlineData(RuntimeIntrinsic.IntegerPopulationCount, CliValueKind.I8,
        WasmOpcodes.I64PopulationCount)]
    [InlineData(RuntimeIntrinsic.IntegerTrailingZeroCount, CliValueKind.I4,
        WasmOpcodes.I32CountTrailingZeros)]
    [InlineData(RuntimeIntrinsic.IntegerTrailingZeroCount, CliValueKind.I8,
        WasmOpcodes.I64CountTrailingZeros)]
    public void EmitsUnaryBitInstruction(
        RuntimeIntrinsic intrinsic,
        CliValueKind argument,
        byte expectedOpcode)
    {
        var request = EmitterTestSupport.CreateRuntimeIntrinsicRequest(
            intrinsic,
            [argument]);

        Emit(request);

        Assert.Contains(expectedOpcode,
            EmitterTestSupport.GetCodeBytes(request.Instruction));
    }

    [Theory]
    [InlineData(RuntimeIntrinsic.IntegerRotateLeft, CliValueKind.I4,
        WasmOpcodes.I32RotateLeft)]
    [InlineData(RuntimeIntrinsic.IntegerRotateLeft, CliValueKind.I8,
        WasmOpcodes.I64RotateLeft)]
    [InlineData(RuntimeIntrinsic.IntegerRotateRight, CliValueKind.I4,
        WasmOpcodes.I32RotateRight)]
    [InlineData(RuntimeIntrinsic.IntegerRotateRight, CliValueKind.I8,
        WasmOpcodes.I64RotateRight)]
    public void EmitsRotateInstruction(
        RuntimeIntrinsic intrinsic,
        CliValueKind argument,
        byte expectedOpcode)
    {
        var request = EmitterTestSupport.CreateRuntimeIntrinsicRequest(
            intrinsic,
            [argument, CliValueKind.I4]);

        Emit(request);

        Assert.Contains(expectedOpcode,
            EmitterTestSupport.GetCodeBytes(request.Instruction));
    }

    [Theory]
    [InlineData(WasmTarget.Wasm32, WasmOpcodes.I32CountLeadingZeros)]
    [InlineData(WasmTarget.Wasm64, WasmOpcodes.I64CountLeadingZeros)]
    public void EmitsNativeWidthInstruction(WasmTarget target, byte expectedOpcode)
    {
        var request = EmitterTestSupport.CreateRuntimeIntrinsicRequest(
            RuntimeIntrinsic.IntegerLeadingZeroCount,
            [CliValueKind.NativeInt]) with
        {
            Target = WasmTargetLayout.For(target),
        };

        Emit(request);

        Assert.Contains(expectedOpcode,
            EmitterTestSupport.GetCodeBytes(request.Instruction));
    }

    [Fact]
    public void RejectsNonIntegerArgument()
    {
        var request = EmitterTestSupport.CreateRuntimeIntrinsicRequest(
            RuntimeIntrinsic.IntegerPopulationCount,
            [CliValueKind.F4]);

        Assert.Throws<InvalidOperationException>(() => Emit(request));
    }

    private static void Emit(RuntimeIntrinsicEmissionRequest request) =>
        new IntegerBitOperationIntrinsicEmitter().Emit(
            request,
            EmitterTestSupport.GetCodeWriter(request.Instruction));
}
