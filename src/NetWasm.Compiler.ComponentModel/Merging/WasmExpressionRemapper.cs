using System.Linq;
using System;

namespace NetWasm.Compiler.ComponentModel.Merging;

// Where one input's indices land in the merged module, by index space; without maps, as an
// input is read, indices are only read past.
internal sealed record WasmMergeIndices(
    int[]? Types, int[]? Functions, int[]? Tables, int[]? Memories, int[]? Globals, int[]? Tags, int[]? Data,
    int[]? Elements)
{
    public static WasmMergeIndices None { get; } = new(null, null, null, null, null, null, null, null);
}

/// <summary>
/// Copies a function body or constant expression of one input into the merged module, rewriting
/// only the immediates that name an index which moved; every other byte is copied as it is.
/// Without indices it only finds where the expression ends.
/// </summary>
internal static class WasmExpressionRemapper
{
    private enum Immediate : byte
    {
        Unsupported, None, End, Skip, Block, BrTable, Function, CallIndirect, Type, Global, Table, MemArg, Memory, F32,
        F64, RefNull, SelectTyped, Tag, TryTable, Misc, Vector, Atomic,
    }

    private enum MiscImmediate : byte
    {
        Unsupported, None, MemoryInit, DataDrop, MemoryCopy, MemoryFill, TableInit, ElementDrop, TableCopy, Table,
    }

    private enum VectorImmediate : byte { None, MemArg, Bytes16, Lane, MemArgLane }

    private enum AtomicImmediate : byte { Unsupported, MemArg, Fence }

    private static readonly Immediate[] Opcodes = OpcodeImmediates();
    private static readonly MiscImmediate[] MiscOpcodes = MiscImmediates();
    private static readonly VectorImmediate[] VectorOpcodes = VectorImmediates();
    private static readonly AtomicImmediate[] AtomicOpcodes = AtomicImmediates();

    // A function body: its locals, then its instructions to the end of the body.
    public static void Body(ReadOnlySpan<byte> code, int start, int length, WasmMergeIndices indices, WasmMergeSink sink)
    {
        var at = start;
        for (var groups = WasmMergeRead.Unsigned(code, ref at); groups > 0; groups--)
        {
            WasmMergeRead.Skip(code, ref at);
            WasmMergeRead.ValueType(code, ref at);
        }
        Instructions(code, start, at, start + length, false, indices, sink);
    }

    // A constant expression, through its end; returns where it ends.
    public static int Constant(ReadOnlySpan<byte> code, int at, WasmMergeIndices indices, WasmMergeSink? sink) =>
        Instructions(code, at, at, code.Length, true, indices, sink);

    private static int Instructions(ReadOnlySpan<byte> code, int copied, int at, int limit, bool constant,
        WasmMergeIndices indices, WasmMergeSink? sink)
    {
        while (at < limit)
        {
            var opcode = WasmMergeRead.Byte(code, ref at);
            switch (Opcodes[opcode])
            {
                case Immediate.None:
                    break;
                case Immediate.End:
                    if (constant)
                    {
                        sink?.Write(code[copied..at]);
                        return at;
                    }
                    break;
                case Immediate.Skip:
                    WasmMergeRead.Skip(code, ref at);
                    break;
                case Immediate.Block:
                    TypeReference(code, ref at, ref copied, indices, sink);
                    break;
                case Immediate.BrTable:
                    for (var labels = WasmMergeRead.Unsigned(code, ref at); labels > 0; labels--)
                        WasmMergeRead.Skip(code, ref at);
                    WasmMergeRead.Skip(code, ref at);
                    break;
                case Immediate.Function:
                    Index(code, ref at, ref copied, indices.Functions, sink);
                    break;
                case Immediate.CallIndirect:
                    Index(code, ref at, ref copied, indices.Types, sink);
                    Index(code, ref at, ref copied, indices.Tables, sink);
                    break;
                case Immediate.Type:
                    Index(code, ref at, ref copied, indices.Types, sink);
                    break;
                case Immediate.Global:
                    Index(code, ref at, ref copied, indices.Globals, sink);
                    break;
                case Immediate.Table:
                    Index(code, ref at, ref copied, indices.Tables, sink);
                    break;
                case Immediate.MemArg:
                    MemArg(code, ref at, ref copied, indices, sink);
                    break;
                case Immediate.Memory:
                    Index(code, ref at, ref copied, indices.Memories, sink);
                    break;
                case Immediate.F32:
                    WasmMergeRead.Bytes(code, ref at, 4);
                    break;
                case Immediate.F64:
                    WasmMergeRead.Bytes(code, ref at, 8);
                    break;
                case Immediate.RefNull:
                    TypeReference(code, ref at, ref copied, indices, sink);
                    break;
                case Immediate.SelectTyped:
                    for (var types = WasmMergeRead.Unsigned(code, ref at); types > 0; types--)
                        WasmMergeRead.ValueType(code, ref at);
                    break;
                case Immediate.Tag:
                    Index(code, ref at, ref copied, indices.Tags, sink);
                    break;
                case Immediate.TryTable:
                    TypeReference(code, ref at, ref copied, indices, sink);
                    for (var catches = WasmMergeRead.Unsigned(code, ref at); catches > 0; catches--)
                    {
                        var kind = WasmMergeRead.Byte(code, ref at);
                        if (kind > 3) throw WasmMergeRead.Invalid($"a core module holds a try_table catch of kind {kind}");
                        if (kind < 2) Index(code, ref at, ref copied, indices.Tags, sink);
                        WasmMergeRead.Skip(code, ref at);
                    }
                    break;
                case Immediate.Misc:
                    Misc(code, ref at, ref copied, indices, sink);
                    break;
                case Immediate.Vector:
                    Vector(code, ref at, ref copied, indices, sink);
                    break;
                case Immediate.Atomic:
                    Atomic(code, ref at, ref copied, indices, sink);
                    break;
                default:
                    throw Unsupported($"0x{opcode:x2}");
            }
        }
        if (constant || at != limit)
            throw WasmMergeRead.Invalid("a core module holds an expression that runs past its end");
        sink!.Write(code[copied..at]);
        return at;
    }

    private static void Misc(ReadOnlySpan<byte> code, ref int at, ref int copied, WasmMergeIndices indices,
        WasmMergeSink? sink)
    {
        var instruction = WasmMergeRead.Unsigned(code, ref at);
        switch (instruction < MiscOpcodes.Length ? MiscOpcodes[instruction] : MiscImmediate.Unsupported)
        {
            case MiscImmediate.None:
                break;
            case MiscImmediate.MemoryInit:
                Index(code, ref at, ref copied, indices.Data, sink);
                Index(code, ref at, ref copied, indices.Memories, sink);
                break;
            case MiscImmediate.DataDrop:
                Index(code, ref at, ref copied, indices.Data, sink);
                break;
            case MiscImmediate.MemoryCopy:
                Index(code, ref at, ref copied, indices.Memories, sink);
                Index(code, ref at, ref copied, indices.Memories, sink);
                break;
            case MiscImmediate.MemoryFill:
                Index(code, ref at, ref copied, indices.Memories, sink);
                break;
            case MiscImmediate.TableInit:
                Index(code, ref at, ref copied, indices.Elements, sink);
                Index(code, ref at, ref copied, indices.Tables, sink);
                break;
            case MiscImmediate.ElementDrop:
                Index(code, ref at, ref copied, indices.Elements, sink);
                break;
            case MiscImmediate.TableCopy:
                Index(code, ref at, ref copied, indices.Tables, sink);
                Index(code, ref at, ref copied, indices.Tables, sink);
                break;
            case MiscImmediate.Table:
                Index(code, ref at, ref copied, indices.Tables, sink);
                break;
            default:
                throw Unsupported($"0xfc {instruction}");
        }
    }

    private static void Vector(ReadOnlySpan<byte> code, ref int at, ref int copied, WasmMergeIndices indices,
        WasmMergeSink? sink)
    {
        var instruction = WasmMergeRead.Unsigned(code, ref at);
        if (instruction >= VectorOpcodes.Length) throw Unsupported($"0xfd {instruction}");
        switch (VectorOpcodes[instruction])
        {
            case VectorImmediate.MemArg:
                MemArg(code, ref at, ref copied, indices, sink);
                break;
            case VectorImmediate.Bytes16:
                WasmMergeRead.Bytes(code, ref at, 16);
                break;
            case VectorImmediate.Lane:
                WasmMergeRead.Byte(code, ref at);
                break;
            case VectorImmediate.MemArgLane:
                MemArg(code, ref at, ref copied, indices, sink);
                WasmMergeRead.Byte(code, ref at);
                break;
        }
    }

    private static void Atomic(ReadOnlySpan<byte> code, ref int at, ref int copied, WasmMergeIndices indices,
        WasmMergeSink? sink)
    {
        var instruction = WasmMergeRead.Unsigned(code, ref at);
        switch (instruction < AtomicOpcodes.Length ? AtomicOpcodes[instruction] : AtomicImmediate.Unsupported)
        {
            case AtomicImmediate.MemArg:
                MemArg(code, ref at, ref copied, indices, sink);
                break;
            case AtomicImmediate.Fence:
                WasmMergeRead.Byte(code, ref at);
                break;
            default:
                throw Unsupported($"0xfe {instruction}");
        }
    }

    // A memory access: its alignment, which carries an explicit memory index when bit 6 is set,
    // and its offset. An access of memory 0 that lands elsewhere gains an explicit index.
    private static void MemArg(ReadOnlySpan<byte> code, ref int at, ref int copied, WasmMergeIndices indices,
        WasmMergeSink? sink)
    {
        var start = at;
        var alignment = WasmMergeRead.Unsigned(code, ref at);
        if ((alignment & 0x40) != 0)
        {
            Index(code, ref at, ref copied, indices.Memories, sink);
        }
        else if (indices.Memories is { } memories && Mapped(memories, 0) is var memory and not 0)
        {
            sink!.Write(code[copied..start]);
            sink.Unsigned(alignment | 0x40);
            sink.Unsigned((uint)memory);
            copied = at;
        }
        WasmMergeRead.Skip(code, ref at);
    }

    // A block type or heap type: an empty block or a value or abstract heap type in one byte, or
    // a type index as a non-negative signed LEB128.
    private static void TypeReference(ReadOnlySpan<byte> code, ref int at, ref int copied, WasmMergeIndices indices,
        WasmMergeSink? sink)
    {
        var first = WasmMergeRead.Byte(code, ref at);
        if (first is 0x63 or 0x64)
            throw WasmMergeRead.Invalid("a core module uses typed references, which the merger does not renumber");
        if (first is >= 0x40 and <= 0x7f) return;
        at--;
        Index(code, ref at, ref copied, indices.Types, sink, signed: true);
    }

    private static void Index(ReadOnlySpan<byte> code, ref int at, ref int copied, int[]? map, WasmMergeSink? sink,
        bool signed = false)
    {
        var start = at;
        var value = WasmMergeRead.Unsigned(code, ref at);
        if (map is null) return;
        var mapped = (uint)Mapped(map, value);
        if (mapped == value) return;
        sink!.Write(code[copied..start]);
        if (signed) sink.Signed(mapped);
        else sink.Unsigned(mapped);
        copied = at;
    }

    public static int Mapped(int[] map, uint index) =>
        index < map.Length ? map[index] : throw WasmMergeRead.Invalid($"a core module refers to index {index}, which it does not define");

    private static Core.CompilerException Unsupported(string instruction) =>
        WasmMergeRead.Invalid($"a core module uses instruction {instruction}, which the merger does not copy");

    private static Immediate[] OpcodeImmediates()
    {
        var kinds = new Immediate[256];
        void Set(int from, int to, Immediate kind) => Array.Fill(kinds, kind, from, to - from + 1);
        Set(0x00, 0x01, Immediate.None);
        Set(0x02, 0x04, Immediate.Block);
        Set(0x05, 0x05, Immediate.None);
        Set(0x06, 0x06, Immediate.Block);
        Set(0x07, 0x08, Immediate.Tag);
        Set(0x09, 0x09, Immediate.Skip);
        Set(0x0a, 0x0a, Immediate.None);
        Set(0x0b, 0x0b, Immediate.End);
        Set(0x0c, 0x0d, Immediate.Skip);
        Set(0x0e, 0x0e, Immediate.BrTable);
        Set(0x0f, 0x0f, Immediate.None);
        Set(0x10, 0x10, Immediate.Function);
        Set(0x11, 0x11, Immediate.CallIndirect);
        Set(0x12, 0x12, Immediate.Function);
        Set(0x13, 0x13, Immediate.CallIndirect);
        Set(0x14, 0x15, Immediate.Type);
        Set(0x18, 0x18, Immediate.Skip);
        Set(0x19, 0x1b, Immediate.None);
        Set(0x1c, 0x1c, Immediate.SelectTyped);
        Set(0x1f, 0x1f, Immediate.TryTable);
        Set(0x20, 0x22, Immediate.Skip);
        Set(0x23, 0x24, Immediate.Global);
        Set(0x25, 0x26, Immediate.Table);
        Set(0x28, 0x3e, Immediate.MemArg);
        Set(0x3f, 0x40, Immediate.Memory);
        Set(0x41, 0x42, Immediate.Skip);
        Set(0x43, 0x43, Immediate.F32);
        Set(0x44, 0x44, Immediate.F64);
        Set(0x45, 0xc4, Immediate.None);
        Set(0xd0, 0xd0, Immediate.RefNull);
        Set(0xd1, 0xd1, Immediate.None);
        Set(0xd2, 0xd2, Immediate.Function);
        Set(0xd3, 0xd4, Immediate.None);
        Set(0xd5, 0xd6, Immediate.Skip);
        Set(0xfc, 0xfc, Immediate.Misc);
        Set(0xfd, 0xfd, Immediate.Vector);
        Set(0xfe, 0xfe, Immediate.Atomic);
        return kinds;
    }

    private static MiscImmediate[] MiscImmediates() =>
    [
        .. Enumerable.Repeat(MiscImmediate.None, 8), MiscImmediate.MemoryInit, MiscImmediate.DataDrop,
        MiscImmediate.MemoryCopy, MiscImmediate.MemoryFill, MiscImmediate.TableInit, MiscImmediate.ElementDrop,
        MiscImmediate.TableCopy, MiscImmediate.Table, MiscImmediate.Table, MiscImmediate.Table,
    ];

    // The SIMD instructions through relaxed SIMD: loads and stores take a memory access, lane
    // loads and stores a lane after it, v128.const and i8x16.shuffle 16 bytes, lane
    // instructions a lane, and the rest nothing.
    private static VectorImmediate[] VectorImmediates()
    {
        var kinds = new VectorImmediate[0x114];
        Array.Fill(kinds, VectorImmediate.MemArg, 0, 12);
        Array.Fill(kinds, VectorImmediate.Bytes16, 12, 2);
        Array.Fill(kinds, VectorImmediate.Lane, 21, 14);
        Array.Fill(kinds, VectorImmediate.MemArgLane, 84, 8);
        Array.Fill(kinds, VectorImmediate.MemArg, 92, 2);
        return kinds;
    }

    private static AtomicImmediate[] AtomicImmediates()
    {
        var kinds = new AtomicImmediate[0x4f];
        Array.Fill(kinds, AtomicImmediate.MemArg, 0, 3);
        kinds[3] = AtomicImmediate.Fence;
        Array.Fill(kinds, AtomicImmediate.MemArg, 0x10, 0x3f);
        return kinds;
    }
}
