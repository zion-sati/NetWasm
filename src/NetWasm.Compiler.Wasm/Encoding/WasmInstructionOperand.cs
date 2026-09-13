using System;
using System.Collections.Immutable;

namespace NetWasm.Compiler.Wasm.Encoding;

#pragma warning disable CA1720 // Factory names intentionally describe WebAssembly value encodings.
/// <summary>
/// Immutable operand data carried by a <see cref="WasmInstruction"/> command.
/// </summary>
public sealed record WasmInstructionOperand
{
    private WasmInstructionOperand(
        WasmInstructionOperandShape shape,
        byte byteValue = 0,
        uint unsignedValue = 0,
        int signedValue = 0,
        ulong unsigned64Value = 0,
        long signed64Value = 0,
        float float32Value = 0,
        double float64Value = 0,
        ImmutableArray<byte> bytes = default,
        string? text = null,
        uint alignment = 0,
        uint offset = 0,
        uint third = 0,
        byte blockTypeValue = 0,
        uint tagIndex = 0,
        uint labelDepth = 0)
    {
        Shape = shape;
        ByteValue = byteValue;
        UnsignedValue = unsignedValue;
        SignedValue = signedValue;
        Unsigned64Value = unsigned64Value;
        Signed64Value = signed64Value;
        Float32Value = float32Value;
        Float64Value = float64Value;
        Bytes = bytes.IsDefault ? ImmutableArray<byte>.Empty : bytes;
        Text = text;
        Alignment = alignment;
        Offset = offset;
        Third = third;
        BlockTypeValue = blockTypeValue;
        TagIndex = tagIndex;
        LabelDepth = labelDepth;
    }

    public WasmInstructionOperandShape Shape { get; }

    public byte ByteValue { get; }

    public uint UnsignedValue { get; }

    public int SignedValue { get; }

    public ulong Unsigned64Value { get; }

    public long Signed64Value { get; }

    public float Float32Value { get; }

    public double Float64Value { get; }

    public ImmutableArray<byte> Bytes { get; }

    public string? Text { get; }

    public uint Alignment { get; }

    public uint Offset { get; }

    public uint Third { get; }

    public byte BlockTypeValue { get; }

    public uint TagIndex { get; }

    public uint LabelDepth { get; }

    public static WasmInstructionOperand None { get; } =
        new(WasmInstructionOperandShape.None);

    public static WasmInstructionOperand Byte(byte value) =>
        new(WasmInstructionOperandShape.Byte, byteValue: value);

    public static WasmInstructionOperand BlockType(byte value) =>
        new(WasmInstructionOperandShape.BlockType, blockTypeValue: value);

    public static WasmInstructionOperand Unsigned(uint value) =>
        new(WasmInstructionOperandShape.UnsignedLeb128, unsignedValue: value);

    public static WasmInstructionOperand Signed(int value) =>
        new(WasmInstructionOperandShape.SignedLeb128, signedValue: value);

    public static WasmInstructionOperand Unsigned64(ulong value) =>
        new(WasmInstructionOperandShape.UnsignedLeb12864, unsigned64Value: value);

    public static WasmInstructionOperand Signed64(long value) =>
        new(WasmInstructionOperandShape.SignedLeb12864, signed64Value: value);

    public static WasmInstructionOperand Float32(float value) =>
        new(WasmInstructionOperandShape.Float32, float32Value: value);

    public static WasmInstructionOperand Float64(double value) =>
        new(WasmInstructionOperandShape.Float64, float64Value: value);

    public static WasmInstructionOperand FromBytes(ReadOnlySpan<byte> value) =>
        new(WasmInstructionOperandShape.Bytes, bytes: ImmutableArray.Create(value.ToArray()));

    public static WasmInstructionOperand String(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return new(WasmInstructionOperandShape.String, text: value);
    }

    public static WasmInstructionOperand Memory(uint alignment, uint offset) =>
        new(
            WasmInstructionOperandShape.MemoryArgument,
            alignment: alignment,
            offset: offset);

    public static WasmInstructionOperand Prefixed(uint subopcode) =>
        new(
            WasmInstructionOperandShape.PrefixedUnsignedLeb128,
            unsignedValue: subopcode);

    public static WasmInstructionOperand PrefixedPair(uint first, uint second) =>
        new(
            WasmInstructionOperandShape.PrefixedUnsignedLeb128Pair,
            alignment: first,
            offset: second);

    public static WasmInstructionOperand PrefixedTriple(
        uint first,
        uint second,
        uint third) =>
        new(
            WasmInstructionOperandShape.PrefixedUnsignedLeb128Triple,
            alignment: first,
            offset: second,
            third: third);

    public static WasmInstructionOperand TryTableCatch(
        byte blockType,
        uint tagIndex,
        uint labelDepth) =>
        new(
            WasmInstructionOperandShape.TryTableCatch,
            blockTypeValue: blockType,
            tagIndex: tagIndex,
            labelDepth: labelDepth);
}
#pragma warning restore CA1720
