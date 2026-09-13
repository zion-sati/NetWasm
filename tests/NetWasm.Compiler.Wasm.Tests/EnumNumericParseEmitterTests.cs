using System;
using System.Collections.Generic;
using System.Linq;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission.Support;
using NetWasm.Compiler.Wasm.Emission.Instructions.Runtime;
using NetWasm.Compiler.Wasm.Encoding;
using Xunit;

namespace NetWasm.Compiler.Wasm.Tests;

public sealed class EnumNumericParseEmitterTests
{
    [Theory]
    [InlineData("i1", CliValueKind.I4)]
    [InlineData("u1", CliValueKind.I4)]
    [InlineData("i2", CliValueKind.I4)]
    [InlineData("u2", CliValueKind.I4)]
    [InlineData("char", CliValueKind.I4)]
    [InlineData("i4", CliValueKind.I4)]
    [InlineData("u4", CliValueKind.I4)]
    [InlineData("i8", CliValueKind.I8)]
    [InlineData("u8", CliValueKind.I8)]
    public void EmitsTheDecimalLoopAndExplicitResultContract(
        string underlyingName,
        CliValueKind stackKind)
    {
        var layouts = new TestLayouts(WasmTarget.Wasm32);
        var writer = new RecordingInstructionWriter();
        var emitter = Assert.IsAssignableFrom<IEnumNumericParseEmitter>(
            new EnumNumericParseEmitter(
                layouts,
                new AddressInstructionEmitter(layouts),
                layouts));

        emitter.Emit(Request(underlyingName, stackKind), writer);

        Assert.Contains(writer.Instructions, instruction => instruction.Opcode == WasmOpcodes.Block);
        Assert.Contains(writer.Instructions, instruction => instruction.Opcode == WasmOpcodes.Loop);
        Assert.Contains(writer.Instructions, instruction => instruction.Opcode == WasmOpcodes.I32Load16Unsigned);
        Assert.Contains(writer.Instructions, instruction => instruction.Opcode == WasmOpcodes.I32LessThanUnsigned);
        Assert.Contains(writer.Instructions, instruction =>
            instruction.Opcode == WasmOpcodes.I32Constant &&
            instruction.Operand.SignedValue == (int)EnumNumericParseStatus.Success);
        Assert.Contains(writer.Instructions, instruction =>
            instruction.Opcode == WasmOpcodes.I32Constant &&
            instruction.Operand.SignedValue == (int)EnumNumericParseStatus.NonNumeric);
        Assert.Contains(writer.Instructions, instruction =>
            instruction.Opcode == WasmOpcodes.I32Constant &&
            instruction.Operand.SignedValue == (int)EnumNumericParseStatus.Invalid);
        Assert.Contains(writer.Instructions, instruction =>
            instruction.Opcode == WasmOpcodes.I32Constant &&
            instruction.Operand.SignedValue == (int)EnumNumericParseStatus.Overflow);
        Assert.NotEmpty(Encode(writer.Instructions));
    }

    [Theory]
    [InlineData(WasmTarget.Wasm32, WasmOpcodes.I32Multiply, WasmOpcodes.I32Constant)]
    [InlineData(WasmTarget.Wasm64, WasmOpcodes.I64Multiply, WasmOpcodes.I64Constant)]
    public void CharacterAddressingUsesTheSelectedReferenceWidth(
        WasmTarget target,
        byte multiply,
        byte constant)
    {
        var layouts = new TestLayouts(target);
        var writer = new RecordingInstructionWriter();
        var emitter = Assert.IsAssignableFrom<IEnumNumericParseEmitter>(
            new EnumNumericParseEmitter(
                layouts,
                new AddressInstructionEmitter(layouts),
                layouts));

        emitter.Emit(Request("i4", CliValueKind.I4), writer);

        Assert.Contains(writer.Instructions, instruction => instruction.Opcode == multiply);
        Assert.Contains(writer.Instructions, instruction =>
            instruction.Opcode == constant &&
            (target == WasmTarget.Wasm32
                ? instruction.Operand.SignedValue == sizeof(char)
                : instruction.Operand.Signed64Value == sizeof(char)));
    }

    [Fact]
    public void DecimalDigitsAreNormalizedBeforeTheOverflowArithmetic()
    {
        var layouts = new TestLayouts(WasmTarget.Wasm32);
        var writer = new RecordingInstructionWriter();
        var emitter = Assert.IsAssignableFrom<IEnumNumericParseEmitter>(
            new EnumNumericParseEmitter(
                layouts,
                new AddressInstructionEmitter(layouts),
                layouts));

        emitter.Emit(Request("i4", CliValueKind.I4), writer);

        var subtraction = writer.Instructions.FindIndex(instruction =>
            instruction.Opcode == WasmOpcodes.I32Subtract &&
            writer.Instructions.IndexOf(instruction) > 0);
        Assert.True(subtraction >= 3);
        Assert.Equal(WasmOpcodes.LocalGet, writer.Instructions[subtraction - 2].Opcode);
        Assert.Equal(4u, writer.Instructions[subtraction - 2].Operand.UnsignedValue);
        Assert.Equal(WasmOpcodes.I32Constant, writer.Instructions[subtraction - 1].Opcode);
        Assert.Equal('0', writer.Instructions[subtraction - 1].Operand.SignedValue);
        Assert.Equal(WasmOpcodes.LocalSet, writer.Instructions[subtraction + 1].Opcode);
        Assert.Equal(4u, writer.Instructions[subtraction + 1].Operand.UnsignedValue);
    }

    [Fact]
    public void ReadsCharactersFromArrayDataWhenRequested()
    {
        var layouts = new TestLayouts(WasmTarget.Wasm32);
        var writer = new RecordingInstructionWriter();
        var emitter = Assert.IsAssignableFrom<IEnumNumericParseEmitter>(
            new EnumNumericParseEmitter(
                layouts,
                new AddressInstructionEmitter(layouts),
                layouts));

        emitter.Emit(Request("i4", CliValueKind.I4) with { IsArray = true }, writer);

        Assert.Contains(writer.Instructions, instruction =>
            instruction.Opcode == WasmOpcodes.I32Load &&
            instruction.Operand.Offset == (uint)layouts.ArrayDataPointerOffset);
        Assert.Contains(writer.Instructions, instruction =>
            instruction.Opcode == WasmOpcodes.I32Load16Unsigned &&
            instruction.Operand.Offset == 0);
    }

    [Fact]
    public void RejectsInvalidRequestsAtTheCapabilityBoundary()
    {
        var layouts = new TestLayouts(WasmTarget.Wasm32);
        var emitter = Assert.IsAssignableFrom<IEnumNumericParseEmitter>(
            new EnumNumericParseEmitter(
                layouts,
                new AddressInstructionEmitter(layouts),
                layouts));
        var writer = new RecordingInstructionWriter();

        Assert.Throws<ArgumentException>(() => emitter.Emit(
            Request("f4", CliValueKind.F4), writer));
        Assert.Throws<ArgumentOutOfRangeException>(() => emitter.Emit(
            Request("i4", CliValueKind.I4) with { IndexLocal = -1 }, writer));
        Assert.Throws<ArgumentException>(() => emitter.Emit(
            Request("i4", CliValueKind.I4) with
            {
                Output = new EnumNumericParseOutput(1, 1),
            }, writer));
    }

    [Fact]
    public void RejectsNullDependenciesAndInvocationArguments()
    {
        var layouts = new TestLayouts(WasmTarget.Wasm32);
        var request = Request("i4", CliValueKind.I4);
        var writer = new RecordingInstructionWriter();

        Assert.Throws<ArgumentNullException>(() => new EnumNumericParseEmitter(
            null!, new AddressInstructionEmitter(layouts), layouts));
        Assert.Throws<ArgumentNullException>(() => new EnumNumericParseEmitter(
            layouts, null!, layouts));
        Assert.Throws<ArgumentNullException>(() => new EnumNumericParseEmitter(
            layouts, new AddressInstructionEmitter(layouts), null!));

        var emitter = Assert.IsAssignableFrom<IEnumNumericParseEmitter>(
            new EnumNumericParseEmitter(
                layouts,
                new AddressInstructionEmitter(layouts),
                layouts));
        Assert.Throws<ArgumentNullException>(() => emitter.Emit(null!, writer));
        Assert.Throws<ArgumentNullException>(() => emitter.Emit(request, null!));
    }

    [Fact]
    public void StatusValuesAreStableForParseAndTryParseIntegration()
    {
        Assert.Equal(0, (int)EnumNumericParseStatus.Success);
        Assert.Equal(1, (int)EnumNumericParseStatus.NonNumeric);
        Assert.Equal(2, (int)EnumNumericParseStatus.Invalid);
        Assert.Equal(3, (int)EnumNumericParseStatus.Overflow);
    }

    private static EnumNumericParseEmissionRequest Request(
        string underlyingName,
        CliValueKind stackKind) => new(
        CliTypeIdentity.Primitive(underlyingName, stackKind),
        TextLocal: 0,
        Output: new EnumNumericParseOutput(ValueLocal: 1, StatusLocal: 2),
        IndexLocal: 3,
        DigitLocal: 4);

    private static byte[] Encode(IReadOnlyList<WasmInstruction> instructions)
    {
        var buffer = new WasmBinaryBuffer();
        var binary = new WasmBinaryWriter(buffer);
        var writer = new WasmInstructionWriter(binary);
        foreach (var instruction in instructions)
            writer.Write(instruction);
        return new WasmBinarySnapshotReader(buffer).Read();
    }

    private sealed class RecordingInstructionWriter : IWasmInstructionWriter
    {
        public List<WasmInstruction> Instructions { get; } = [];

        public void Write(WasmInstruction instruction)
        {
            ArgumentNullException.ThrowIfNull(instruction);
            Instructions.Add(instruction);
        }
    }

    private sealed class TestLayouts(WasmTarget target) :
        ITargetLayout,
        IRuntimeObjectLayout
    {
        public WasmTargetLayout Target { get; } = WasmTargetLayout.For(target);

        public int StringLengthOffset => 4;
        public int StringDataOffset => 8;
        public int ArrayLengthOffset => 12;
        public int ArrayDataPointerOffset => 16;
        public int ArrayElementTypeIdOffset => 20;
        public int DelegateTargetOffset => 24;
        public int DelegateMethodIdOffset => 28;
        public int DelegateLeftOffset => 32;
        public int DelegateRightOffset => 36;
    }
}
