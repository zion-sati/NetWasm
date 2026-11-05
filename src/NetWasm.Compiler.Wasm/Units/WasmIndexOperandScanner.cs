using System;
using System.Collections.Immutable;
using System.IO;

namespace NetWasm.Compiler.Wasm.Units;

/// <summary>
/// Decodes a function body by the WebAssembly binary format to find the module indices
/// it encodes. It covers the instruction set the compiler emits: the core instructions,
/// tail calls, exception handling (try_table and the legacy forms), bulk memory and
/// saturating truncation (0xFC), SIMD (0xFD), threads (0xFE) and typed references
/// without GC heap types. Anything else is rejected rather than guessed.
/// </summary>
internal sealed class WasmIndexOperandScanner : IWasmIndexOperandScanner
{
    public ImmutableArray<WasmIndexOperand> Scan(ReadOnlySpan<byte> body)
    {
        var reader = new BodyReader(body);
        var operands = ImmutableArray.CreateBuilder<WasmIndexOperand>();
        var groups = reader.Unsigned();
        for (var group = 0u; group < groups; group++)
        {
            reader.Unsigned();
            reader.ValueType();
        }
        while (!reader.AtEnd)
        {
            Instruction(ref reader, operands);
        }
        return operands.ToImmutable();
    }

    private static void Instruction(ref BodyReader reader, ImmutableArray<WasmIndexOperand>.Builder operands)
    {
        var start = reader.Position;
        var opcode = reader.Byte();
        switch (opcode)
        {
            case 0x00: // unreachable
            case 0x01: // nop
            case 0x05: // else
            case 0x0A: // throw_ref
            case 0x0B: // end
            case 0x0F: // return
            case 0x19: // catch_all
            case 0x1A: // drop
            case 0x1B: // select
            case 0xD1: // ref.is_null
            case 0xD3: // ref.eq
            case 0xD4: // ref.as_non_null
                return;
            case 0x02: // block
            case 0x03: // loop
            case 0x04: // if
            case 0x06: // try
                BlockType(ref reader, operands);
                return;
            case 0x07: // catch
            case 0x08: // throw
                operands.Add(reader.Index(WasmIndexSpace.Tag));
                return;
            case 0x09: // rethrow
            case 0x0C: // br
            case 0x0D: // br_if
            case 0x18: // delegate
            case 0xD5: // br_on_null
            case 0xD6: // br_on_non_null
                reader.Unsigned();
                return;
            case 0x0E: // br_table
                {
                    var count = reader.Unsigned();
                    for (var label = 0u; label <= count; label++) reader.Unsigned();
                    return;
                }
            case 0x10: // call
            case 0x12: // return_call
            case 0xD2: // ref.func
                operands.Add(reader.Index(WasmIndexSpace.Function));
                return;
            case 0x11: // call_indirect
            case 0x13: // return_call_indirect
                operands.Add(reader.Index(WasmIndexSpace.Type));
                operands.Add(reader.Index(WasmIndexSpace.Table));
                return;
            case 0x14: // call_ref
            case 0x15: // return_call_ref
                operands.Add(reader.Index(WasmIndexSpace.Type));
                return;
            case 0x1C: // select t*
                {
                    var count = reader.Unsigned();
                    for (var type = 0u; type < count; type++) reader.ValueType();
                    return;
                }
            case 0x1F: // try_table
                BlockType(ref reader, operands);
                TryTableCatches(ref reader, operands);
                return;
            case 0x20: // local.get
            case 0x21: // local.set
            case 0x22: // local.tee
                reader.Unsigned();
                return;
            case 0x23: // global.get
            case 0x24: // global.set
                operands.Add(reader.Index(WasmIndexSpace.Global));
                return;
            case 0x25: // table.get
            case 0x26: // table.set
                operands.Add(reader.Index(WasmIndexSpace.Table));
                return;
            case 0x3F: // memory.size
            case 0x40: // memory.grow
                operands.Add(reader.Index(WasmIndexSpace.Memory));
                return;
            case 0x41: // i32.const
            case 0x42: // i64.const
                reader.Signed();
                return;
            case 0x43: // f32.const
                reader.Skip(4);
                return;
            case 0x44: // f64.const
                reader.Skip(8);
                return;
            case 0xD0: // ref.null
                HeapType(ref reader, operands);
                return;
            case 0xFC:
                MiscellaneousInstruction(ref reader, operands, start);
                return;
            case 0xFD:
                SimdInstruction(ref reader, operands);
                return;
            case 0xFE:
                AtomicInstruction(ref reader, operands, start);
                return;
        }
        if (opcode is >= 0x28 and <= 0x3E)
        {
            MemoryArgument(ref reader, operands);
            return;
        }
        if (opcode is >= 0x45 and <= 0xC4)
        {
            return;
        }
        throw Unsupported($"opcode 0x{opcode:x2}", start);
    }

    private static void BlockType(ref BodyReader reader, ImmutableArray<WasmIndexOperand>.Builder operands)
    {
        var start = reader.Position;
        var value = reader.Signed();
        if (value >= 0)
        {
            operands.Add(new(start, reader.Position - start, WasmIndexSpace.Type, checked((uint)value), true));
            return;
        }
        if (value is -0x1D or -0x1C) // (ref null ht), (ref ht)
        {
            throw Unsupported("a GC reference block type", start);
        }
    }

    private static void HeapType(ref BodyReader reader, ImmutableArray<WasmIndexOperand>.Builder operands)
    {
        var start = reader.Position;
        var value = reader.Signed();
        if (value >= 0)
        {
            operands.Add(new(start, reader.Position - start, WasmIndexSpace.Type, checked((uint)value), true));
        }
    }

    private static void TryTableCatches(ref BodyReader reader, ImmutableArray<WasmIndexOperand>.Builder operands)
    {
        var count = reader.Unsigned();
        for (var clause = 0u; clause < count; clause++)
        {
            var start = reader.Position;
            switch (reader.Byte())
            {
                case 0x00: // catch tag label
                case 0x01: // catch_ref tag label
                    operands.Add(reader.Index(WasmIndexSpace.Tag));
                    reader.Unsigned();
                    break;
                case 0x02: // catch_all label
                case 0x03: // catch_all_ref label
                    reader.Unsigned();
                    break;
                default:
                    throw Unsupported("a try_table catch kind", start);
            }
        }
    }

    private static void MemoryArgument(ref BodyReader reader, ImmutableArray<WasmIndexOperand>.Builder operands)
    {
        var alignment = reader.Unsigned();
        if ((alignment & 0x40) != 0)
        {
            operands.Add(reader.Index(WasmIndexSpace.Memory));
        }
        reader.Unsigned64();
    }

    private static void MiscellaneousInstruction(
        ref BodyReader reader, ImmutableArray<WasmIndexOperand>.Builder operands, int start)
    {
        var subopcode = reader.Unsigned();
        switch (subopcode)
        {
            case <= 7: // saturating truncation
                return;
            case 8: // memory.init
                operands.Add(reader.Index(WasmIndexSpace.Data));
                operands.Add(reader.Index(WasmIndexSpace.Memory));
                return;
            case 9: // data.drop
                operands.Add(reader.Index(WasmIndexSpace.Data));
                return;
            case 10: // memory.copy
                operands.Add(reader.Index(WasmIndexSpace.Memory));
                operands.Add(reader.Index(WasmIndexSpace.Memory));
                return;
            case 11: // memory.fill
                operands.Add(reader.Index(WasmIndexSpace.Memory));
                return;
            case 12: // table.init
                operands.Add(reader.Index(WasmIndexSpace.Element));
                operands.Add(reader.Index(WasmIndexSpace.Table));
                return;
            case 13: // elem.drop
                operands.Add(reader.Index(WasmIndexSpace.Element));
                return;
            case 14: // table.copy
                operands.Add(reader.Index(WasmIndexSpace.Table));
                operands.Add(reader.Index(WasmIndexSpace.Table));
                return;
            case 15: // table.grow
            case 16: // table.size
            case 17: // table.fill
                operands.Add(reader.Index(WasmIndexSpace.Table));
                return;
            default:
                throw Unsupported($"0xFC subopcode {subopcode}", start);
        }
    }

    private static void SimdInstruction(
        ref BodyReader reader, ImmutableArray<WasmIndexOperand>.Builder operands)
    {
        var subopcode = reader.Unsigned();
        switch (subopcode)
        {
            case <= 11: // loads, splat loads and v128.store
            case 92: // v128.load32_zero
            case 93: // v128.load64_zero
                MemoryArgument(ref reader, operands);
                return;
            case 12: // v128.const
            case 13: // i8x16.shuffle
                reader.Skip(16);
                return;
            case >= 21 and <= 34: // extract_lane and replace_lane
                reader.Byte();
                return;
            case >= 84 and <= 91: // load_lane and store_lane
                MemoryArgument(ref reader, operands);
                reader.Byte();
                return;
            default:
                // Every other SIMD instruction, relaxed SIMD included, has no immediate.
                return;
        }
    }

    private static void AtomicInstruction(
        ref BodyReader reader, ImmutableArray<WasmIndexOperand>.Builder operands, int start)
    {
        var subopcode = reader.Unsigned();
        if (subopcode == 3) // atomic.fence
        {
            if (reader.Byte() != 0) throw Unsupported("an atomic.fence ordering", start);
            return;
        }
        if (subopcode > 0x4E) throw Unsupported($"0xFE subopcode {subopcode}", start);
        MemoryArgument(ref reader, operands);
    }

    private static InvalidDataException Unsupported(string what, int offset) =>
        new($"The function body has {what} at byte {offset}, which unit relocation cannot decode.");

    private ref struct BodyReader(ReadOnlySpan<byte> bytes)
    {
        private readonly ReadOnlySpan<byte> _bytes = bytes;

        public int Position { get; private set; }

        public readonly bool AtEnd => Position >= _bytes.Length;

        public byte Byte()
        {
            if (Position >= _bytes.Length) throw Truncated();
            return _bytes[Position++];
        }

        public void Skip(int count)
        {
            if (Position + count > _bytes.Length) throw Truncated();
            Position += count;
        }

        public uint Unsigned()
        {
            var value = Unsigned64();
            if (value > uint.MaxValue) throw new InvalidDataException("A u32 LEB128 overflows.");
            return (uint)value;
        }

        public ulong Unsigned64()
        {
            ulong value = 0;
            for (var shift = 0; shift < 70; shift += 7)
            {
                var next = Byte();
                value |= (ulong)(next & 0x7F) << shift;
                if ((next & 0x80) == 0) return value;
            }
            throw new InvalidDataException("A LEB128 is longer than ten bytes.");
        }

        public long Signed()
        {
            long value = 0;
            var shift = 0;
            byte next;
            do
            {
                if (shift >= 70) throw new InvalidDataException("A LEB128 is longer than ten bytes.");
                next = Byte();
                value |= (long)(next & 0x7F) << shift;
                shift += 7;
            }
            while ((next & 0x80) != 0);
            if (shift < 64 && (next & 0x40) != 0) value |= -1L << shift;
            return value;
        }

        public WasmIndexOperand Index(WasmIndexSpace space)
        {
            var start = Position;
            var value = Unsigned();
            return new(start, Position - start, space, value);
        }

        public void ValueType()
        {
            var start = Position;
            var type = Byte();
            if (type is 0x63 or 0x64) throw Unsupported("a GC reference value type", start);
        }

        private readonly InvalidDataException Truncated() =>
            new($"The function body ends inside an instruction at byte {Position}.");
    }
}
