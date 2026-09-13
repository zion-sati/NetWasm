global using Encoding = System.Text.Encoding;

using System;
using System.Collections.Generic;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Tests;

public sealed class WasmInstructionEncodingTests
{
    public static TheoryData<WasmInstruction, byte[]> EveryOperandShape => new()
    {
        { new WasmInstruction(0x01), [0x01] },
        {
            WasmInstruction.WithOperand(0x02, WasmInstructionOperand.BlockType(0x40)),
            [0x02, 0x40]
        },
        {
            WasmInstruction.WithOperand(0x03, WasmInstructionOperand.Unsigned(128)),
            [0x03, 0x80, 0x01]
        },
        {
            WasmInstruction.WithOperand(0x04, WasmInstructionOperand.Signed(-1)),
            [0x04, 0x7f]
        },
        {
            WasmInstruction.WithOperand(0x05, WasmInstructionOperand.Unsigned64(128)),
            [0x05, 0x80, 0x01]
        },
        {
            WasmInstruction.WithOperand(0x06, WasmInstructionOperand.Signed64(-64)),
            [0x06, 0x40]
        },
        {
            WasmInstruction.WithOperand(0x07, WasmInstructionOperand.Float32(1f)),
            [0x07, .. BitConverter.GetBytes(1f)]
        },
        {
            WasmInstruction.WithOperand(0x08, WasmInstructionOperand.Float64(1d)),
            [0x08, .. BitConverter.GetBytes(1d)]
        },
        {
            WasmInstruction.WithOperand(0x09, WasmInstructionOperand.FromBytes([0xaa, 0xbb])),
            [0x09, 0xaa, 0xbb]
        },
        {
            WasmInstruction.WithOperand(0x0a, WasmInstructionOperand.String("AΩ")),
            [0x0a, 0x03, 0x41, 0xce, 0xa9]
        },
        {
            WasmInstruction.WithOperand(0x0b, WasmInstructionOperand.Memory(2, 12)),
            [0x0b, 0x02, 0x0c]
        },
        {
            WasmInstruction.WithOperand(0x0c, WasmInstructionOperand.Prefixed(624485)),
            [0x0c, 0xe5, 0x8e, 0x26]
        },
        {
            WasmInstruction.WithOperand(0x0d, WasmInstructionOperand.PrefixedPair(10, 0)),
            [0x0d, 0x0a, 0x00]
        },
        {
            WasmInstruction.WithOperand(0x0e, WasmInstructionOperand.PrefixedTriple(10, 0, 0)),
            [0x0e, 0x0a, 0x00, 0x00]
        },
        {
            WasmInstruction.WithOperand(
                0x0f,
                WasmInstructionOperand.TryTableCatch(0x40, 3, 4)),
            [0x0f, 0x40, 0x01, 0x00, 0x03, 0x04]
        },
        {
            WasmInstruction.WithOperand(0x10, WasmInstructionOperand.Byte(0x2a)),
            [0x10, 0x2a]
        },
    };

    [Theory]
    [MemberData(nameof(EveryOperandShape))]
    public void DefaultRegistryEncodesEveryOperandShape(
        WasmInstruction instruction,
        byte[] expected)
    {
        var output = new MemoryWriter();
        var writer = new WasmInstructionWriter(output);

        writer.Write(instruction);

        Assert.Equal(expected, output.ToArray());
    }

    [Fact]
    public void BlockTypeShapeEncodesEmptyAndValueResultControls()
    {
        var output = new MemoryWriter();
        var writer = new WasmInstructionWriter(output);

        writer.Write(WasmInstruction.WithOperand(
            WasmOpcodes.Block,
            WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
        writer.Write(WasmInstruction.WithOperand(
            WasmOpcodes.Loop,
            WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
        writer.Write(WasmInstruction.WithOperand(
            WasmOpcodes.If,
            WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
        writer.Write(WasmInstruction.WithOperand(
            WasmOpcodes.Block,
            WasmInstructionOperand.BlockType((byte)WasmValueType.I32)));
        writer.Write(WasmInstruction.WithOperand(
            WasmOpcodes.Block,
            WasmInstructionOperand.BlockType((byte)WasmValueType.I64)));
        writer.Write(WasmInstruction.WithOperand(
            WasmOpcodes.Block,
            WasmInstructionOperand.BlockType((byte)WasmValueType.F32)));
        writer.Write(WasmInstruction.WithOperand(
            WasmOpcodes.Block,
            WasmInstructionOperand.BlockType((byte)WasmValueType.F64)));

        Assert.Equal(
            [
                WasmOpcodes.Block, WasmOpcodes.EmptyBlockType,
                WasmOpcodes.Loop, WasmOpcodes.EmptyBlockType,
                WasmOpcodes.If, WasmOpcodes.EmptyBlockType,
                WasmOpcodes.Block, (byte)WasmValueType.I32,
                WasmOpcodes.Block, (byte)WasmValueType.I64,
                WasmOpcodes.Block, (byte)WasmValueType.F32,
                WasmOpcodes.Block, (byte)WasmValueType.F64,
            ],
            output.ToArray());
    }

    [Fact]
    public void MemoryLoadAndStoreShapesMatchLegacyMemargEncoding()
    {
        var output = new MemoryWriter();
        var writer = new WasmInstructionWriter(output);
        (byte Opcode, uint Alignment)[] instructions =
        [
            (WasmOpcodes.I32Load, 2),
            (WasmOpcodes.I64Load, 3),
            (WasmOpcodes.F32Load, 2),
            (WasmOpcodes.F64Load, 3),
            (WasmOpcodes.I32Load8Signed, 0),
            (WasmOpcodes.I32Load8Unsigned, 0),
            (WasmOpcodes.I32Load16Signed, 1),
            (WasmOpcodes.I32Load16Unsigned, 1),
            (WasmOpcodes.I32Store, 2),
            (WasmOpcodes.I64Store, 3),
            (WasmOpcodes.F32Store, 2),
            (WasmOpcodes.F64Store, 3),
            (WasmOpcodes.I32Store8, 0),
            (WasmOpcodes.I32Store16, 1),
        ];

        foreach (var (opcode, alignment) in instructions)
        {
            writer.Write(WasmInstruction.WithOperand(
                opcode,
                WasmInstructionOperand.Memory(alignment, 128)));
        }

        var expected = new List<byte>();
        foreach (var (opcode, alignment) in instructions)
        {
            expected.AddRange([opcode, (byte)alignment, 0x80, 0x01]);
        }

        Assert.Equal(expected, output.ToArray());
    }

    [Fact]
    public void MemoryCopyFillAndSaturatingPrefixedShapesEncodeAllImmediates()
    {
        var output = new MemoryWriter();
        var writer = new WasmInstructionWriter(output);

        writer.Write(WasmInstruction.WithOperand(
            WasmOpcodes.Prefixed,
            WasmInstructionOperand.PrefixedTriple(WasmOpcodes.MemoryCopy, 0, 0)));
        writer.Write(WasmInstruction.WithOperand(
            WasmOpcodes.Prefixed,
            WasmInstructionOperand.PrefixedPair(WasmOpcodes.MemoryFill, 0)));
        writer.Write(WasmInstruction.WithOperand(
            WasmOpcodes.Prefixed,
            WasmInstructionOperand.Prefixed(WasmOpcodes.I32TruncateSaturateF64Unsigned)));

        Assert.Equal(
            [
                WasmOpcodes.Prefixed, WasmOpcodes.MemoryCopy, 0, 0,
                WasmOpcodes.Prefixed, WasmOpcodes.MemoryFill, 0,
                WasmOpcodes.Prefixed, WasmOpcodes.I32TruncateSaturateF64Unsigned,
            ],
            output.ToArray());
    }

    [Fact]
    public void PrimitiveCapabilitiesEncodeBoundaryValues()
    {
        var output = new MemoryWriter();
        new UnsignedLeb128Encoder().Encode(output, 624485);
        new SignedLeb128Encoder().Encode(output, -624485);
        new UnsignedLeb12864Encoder().Encode(output, ulong.MaxValue);
        new SignedLeb12864Encoder().Encode(output, long.MinValue);

        Assert.Equal(
            [0xe5, 0x8e, 0x26, 0x9b, 0xf1, 0x59,
                0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0x01,
                0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x7f],
            output.ToArray());
    }

    [Fact]
    public void RawBinaryWriterContractWritesOnlyRawBytes()
    {
        var buffer = new WasmBinaryBuffer();
        var concrete = new WasmBinaryWriter(buffer);
        var reader = new WasmBinarySnapshotReader(buffer);
        var memory = new MemoryWriter();

        WriteThroughContract(concrete);
        WriteThroughContract(memory);

        Assert.Equal([0xaa, 0xbb], reader.Read());
        Assert.Equal([0xaa, 0xbb], memory.ToArray());
    }

    private static void WriteThroughContract(IWasmBinaryWriter output) =>
        output.Write([0xaa, 0xbb]);

    [Fact]
    public void InstructionWriterResolvesByShapeAndDelegatesOnce()
    {
        var output = new MemoryWriter();
        var encoder = new RecordingEncoder();
        var registry = new WasmInstructionEncoderRegistry(
            [new(WasmInstructionOperandShape.Byte, encoder)]);
        var writer = new WasmInstructionWriter(output, registry);
        var instruction = WasmInstruction.WithOperand(
            0x7f,
            WasmInstructionOperand.Byte(0x2a));

        writer.Write(instruction);

        Assert.Same(instruction, encoder.Instruction);
        Assert.Same(output, encoder.Writer);
        Assert.Equal(WasmInstructionOperandShape.Byte, encoder.Shape);
    }

    [Fact]
    public void RegistryRejectsDuplicateShapesAndReportsMissingShapes()
    {
        var encoder = new RecordingEncoder();
        Assert.Throws<InvalidOperationException>(() =>
            new WasmInstructionEncoderRegistry(
                [
                    new(WasmInstructionOperandShape.None, encoder),
                    new(WasmInstructionOperandShape.None, encoder),
                ]));

        var registry = new WasmInstructionEncoderRegistry(
            [new(WasmInstructionOperandShape.None, encoder)]);
        Assert.Equal(1, registry.Count);
        var missing = Assert.Throws<InvalidOperationException>(() =>
            registry.Resolve(WasmInstructionOperandShape.Byte));
        Assert.Contains("Byte", missing.Message);
    }

    [Fact]
    public void RegistryRejectsDuplicateAndMissingPrefixedTripleShape()
    {
        var encoder = new RecordingEncoder();
        Assert.Throws<InvalidOperationException>(() =>
            new WasmInstructionEncoderRegistry(
                [
                    new(WasmInstructionOperandShape.PrefixedUnsignedLeb128Triple, encoder),
                    new(WasmInstructionOperandShape.PrefixedUnsignedLeb128Triple, encoder),
                ]));

        var registry = new WasmInstructionEncoderRegistry(
            [new(WasmInstructionOperandShape.None, encoder)]);
        var missing = Assert.Throws<InvalidOperationException>(() =>
            registry.Resolve(WasmInstructionOperandShape.PrefixedUnsignedLeb128Triple));
        Assert.Contains("PrefixedUnsignedLeb128Triple", missing.Message);
    }

    [Fact]
    public void ByteOperandCopiesCallerStorage()
    {
        var source = new byte[] { 1, 2 };
        var operand = WasmInstructionOperand.FromBytes(source);
        source[0] = 9;

        Assert.Equal([1, 2], operand.Bytes.ToArray());
    }

    private sealed class MemoryWriter : IWasmBinaryWriter
    {
        private readonly List<byte> _bytes = [];

        public void Write(ReadOnlySpan<byte> bytes)
        {
            foreach (var value in bytes)
            {
                _bytes.Add(value);
            }
        }

        public byte[] ToArray() => [.. _bytes];
    }

    private sealed class RecordingEncoder : IWasmInstructionEncoder
    {
        public IWasmBinaryWriter? Writer { get; private set; }

        public WasmInstruction? Instruction { get; private set; }

        public WasmInstructionOperandShape Shape { get; private set; }

        public void Encode(IWasmBinaryWriter writer, WasmInstruction instruction)
        {
            Writer = writer;
            Instruction = instruction;
            Shape = instruction.OperandShape;
        }
    }
}
