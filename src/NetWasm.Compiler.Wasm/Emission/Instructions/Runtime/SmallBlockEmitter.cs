using System;
using System.Linq;
using System.Collections.Generic;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission.Methods;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Emission.Instructions.Runtime;

// Wasmtime runs memory.copy and memory.fill out of line, which costs far more
// than the few stores a small block needs. Where the target inlines them,
// blocks of up to 128 bytes are copied and filled inline instead, at most
// eight vector stores; a constant size folds to its own case. A copy loads
// every chunk before it stores any, so overlapping blocks stay correct.
internal static class SmallBlockEmitter
{
    private const int InlineLimit = 128;

    // Every preset zeroes a known size inline, so it stops sooner.
    private const int KnownSizeZeroLimit = 64;
    private const int AlignedCopyChunkLimit = 4;

    public static void EmitCopy(
        IWasmInstructionWriter code,
        WasmTargetLayout target,
        bool inline,
        int destination,
        int source,
        int byteCount)
    {
        if (!inline)
        {
            BulkCopy(code, destination, source, byteCount);
            return;
        }

        var cases = new (long Minimum, int Width, Func<int, long[]> Offsets)[]
        {
            (65, 16, _ => [0, 16, 32, 48, -64, -48, -32, -16]),
            (33, 16, _ => [0, 16, -32, -16]),
            (16, 16, _ => [0, -16]),
            (8, 8, _ => [0, -8]),
            (4, 4, _ => [0, -4]),
            (2, 2, _ => [0, -2]),
            (1, 1, _ => [0]),
        };
        EmitCascade(code, target, byteCount, () => BulkCopy(code, destination, source, byteCount), cases.Length, index =>
        {
            var (_, width, offsets) = cases[index];
            var chunks = offsets(width);
            foreach (var offset in chunks)
            {
                Address(code, target, destination, byteCount, offset);
                Address(code, target, source, byteCount, offset);
                Load(code, width);
            }
            for (var chunk = chunks.Length - 1; chunk >= 0; chunk--)
            {
                Store(code, width);
            }
        }, index => cases[index].Minimum);
    }

    public static void EmitFill(
        IWasmInstructionWriter code,
        WasmTargetLayout target,
        bool inline,
        int destination,
        int value,
        int byteCount)
    {
        if (!inline)
        {
            BulkFill(code, destination, value, byteCount);
            return;
        }

        var cases = new (long Minimum, int Width, long[] Offsets)[]
        {
            (65, 16, [0, 16, 32, 48, -64, -48, -32, -16]),
            (33, 16, [0, 16, -32, -16]),
            (16, 16, [0, -16]),
            (8, 8, [0, -8]),
            (4, 4, [0, -4]),
            (2, 2, [0, -2]),
            (1, 1, [0]),
        };
        EmitCascade(code, target, byteCount, () => BulkFill(code, destination, value, byteCount), cases.Length, index =>
        {
            var (_, width, offsets) = cases[index];
            foreach (var offset in offsets)
            {
                Address(code, target, destination, byteCount, offset);
                Repeated(code, value, width);
                Store(code, width);
            }
        }, index => cases[index].Minimum);
    }

    // Zeroes a block whose size the compiler knows, as initobj and default(T)
    // do: up to 64 bytes, a store of 16, 8, 4, 2 or 1 bytes for each part,
    // which a small struct needs only a few of.
    public static bool TryEmitKnownSizeZero(IWasmInstructionWriter code, int destination, int size)
    {
        if (size > KnownSizeZeroLimit)
        {
            return false;
        }
        for (var written = 0; written < size;)
        {
            var width = (size - written) switch
            {
                >= 16 => 16,
                >= 8 => 8,
                >= 4 => 4,
                >= 2 => 2,
                _ => 1,
            };
            Get(code, destination);
            code.Write(width switch
            {
                16 => WasmInstruction.WithOperand(
                    WasmOpcodes.SimdPrefixed,
                    WasmInstructionOperand.FromBytes([(byte)WasmSimdOpcodes.V128Constant, .. new byte[16]])),
                8 => WasmInstruction.WithOperand(WasmOpcodes.I64Constant, WasmInstructionOperand.Signed64(0)),
                _ => WasmInstruction.WithOperand(WasmOpcodes.I32Constant, WasmInstructionOperand.Signed(0)),
            });
            Store(code, width, (uint)written);
            written += width;
        }
        return true;
    }

    // Fills byteCount bytes at the destination, a whole number of elements of
    // elementSize bytes (2, 4 or 8), with the vector of elements that `pattern`
    // pushes. Chunks overlap at the end, as EmitFill's do; each starts a whole
    // number of elements in, so the elements line up. Over 128 bytes a loop
    // stores 16 bytes at a time and one more store ends at the last byte. The
    // destination and byteCount locals are left changed.
    public static void EmitPatternFill(
        IWasmInstructionWriter code,
        WasmTargetLayout target,
        int destination,
        int byteCount,
        int elementSize,
        Action pattern)
    {
        var cases = new (long Minimum, int Width, long[] Offsets)[]
        {
            (65, 16, [0, 16, 32, 48, -64, -48, -32, -16]),
            (33, 16, [0, 16, -32, -16]),
            (16, 16, [0, -16]),
            (8, 8, [0, -8]),
            (4, 4, [0, -4]),
            (2, 2, [0]),
        }.Where(chunk => chunk.Width >= elementSize).ToArray();
        EmitCascade(code, target, byteCount, () => PatternLoop(code, target, destination, byteCount, pattern), cases.Length, index =>
        {
            var (_, width, offsets) = cases[index];
            foreach (var offset in offsets)
            {
                Address(code, target, destination, byteCount, offset);
                pattern();
                Narrow(code, width);
                Store(code, width);
            }
        }, index => cases[index].Minimum);
    }

    private static void PatternLoop(IWasmInstructionWriter code, WasmTargetLayout target, int destination, int byteCount, Action pattern)
    {
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.Loop, WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
        Get(code, destination);
        pattern();
        Store(code, 16);
        foreach (var local in (ReadOnlySpan<int>)[destination, byteCount])
        {
            Get(code, local);
            Constant(code, target, 16);
            code.Write(WasmInstruction.NoOperand(local == destination
                ? target.UsesMemory64 ? WasmOpcodes.I64Add : WasmOpcodes.I32Add
                : target.UsesMemory64 ? WasmOpcodes.I64Subtract : WasmOpcodes.I32Subtract));
            Set(code, local);
        }
        Get(code, byteCount);
        Constant(code, target, 16);
        code.Write(WasmInstruction.NoOperand(
            target.UsesMemory64 ? WasmOpcodes.I64GreaterThanUnsigned : WasmOpcodes.I32GreaterThanUnsigned));
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.BranchIf, WasmInstructionOperand.Unsigned(0)));
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
        Address(code, target, destination, byteCount, -16);
        pattern();
        Store(code, 16);
    }

    // Narrows a vector of elements to its first `width` bytes, as a scalar for
    // a store of that width.
    private static void Narrow(IWasmInstructionWriter code, int width)
    {
        if (width == 16) return;
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.SimdPrefixed,
            WasmInstructionOperand.PrefixedPair(width == 8 ? WasmSimdOpcodes.I64x2ExtractLane : WasmSimdOpcodes.I32x4ExtractLane, 0)));
    }

    // The widest chunk a known-size copy of the value reads. A value holding a
    // managed byref (a span, or a ref struct around one) lives only in frames,
    // where code stores its fields at their own widths and then copies it at
    // once, as a span is built and then passed; a chunk wider than those fields
    // would read two fresh stores at once, which the processor cannot forward to
    // the load and waits out instead. So such a value's chunks are no wider than
    // its alignment, the widest of its fields. Any other value, boxed, in an
    // array or a static, or a vector that SIMD code stores whole, keeps the
    // widest chunks.
    public static int WidestChunk(ValueLayout value) =>
        value.ByReferenceOffsets.IsEmpty ? 16 : value.Alignment;

    // Copies a value of a known size from the source address on the stack to
    // the destination address beneath it, as memory.copy does. Where the method
    // inlines small blocks the copy runs in exact chunks, widest first, through
    // the context's address scratch; every chunk is loaded before any is stored,
    // so overlapping addresses copy as memory.copy would. No chunk is wider than
    // widestChunk where at most four such chunks cover the value: more chunks
    // cost more than the wait they avoid.
    public static void EmitKnownSizeCopy(
        IWasmInstructionWriter code,
        MethodEmissionContext context,
        int size,
        int widestChunk,
        Action<IWasmInstructionWriter> emitSize)
    {
        if (CanInlineKnownSizeCopy(context, size))
        {
            EmitInlineKnownSizeCopy(code, context, size, widestChunk);
            return;
        }
        emitSize(code);
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.Prefixed,
            WasmInstructionOperand.PrefixedTriple(WasmOpcodes.MemoryCopy, 0, 0)));
    }

    public static bool CanInlineKnownSizeCopy(MethodEmissionContext context, int size) =>
        context.InlineSmallBlocks && size <= InlineLimit && context.CopyDestinationTemporary >= 0 &&
        context.CopySourceTemporary >= 0;

    // Expects the destination and then the source address on the stack.
    public static void EmitInlineKnownSizeCopy(
        IWasmInstructionWriter code, MethodEmissionContext context, int size, int widestChunk)
    {
        if (widestChunk < 1 || (widestChunk & (widestChunk - 1)) != 0)
        {
            throw new ArgumentOutOfRangeException(nameof(widestChunk), widestChunk, "A chunk width is a positive power of two.");
        }
        Set(code, context.CopySourceTemporary);
        Set(code, context.CopyDestinationTemporary);
        var chunks = new List<(int Width, uint Offset)>();
        var offset = 0;
        var widest = size <= AlignedCopyChunkLimit * widestChunk ? widestChunk : 16;
        foreach (var width in (ReadOnlySpan<int>)[16, 8, 4, 2, 1])
        {
            if (width > widest)
            {
                continue;
            }
            for (; size - offset >= width; offset += width)
            {
                chunks.Add((width, (uint)offset));
            }
        }
        foreach (var (width, chunkOffset) in chunks)
        {
            Get(code, context.CopyDestinationTemporary);
            Get(code, context.CopySourceTemporary);
            Load(code, width, chunkOffset);
        }
        for (var index = chunks.Count - 1; index >= 0; index--)
        {
            Store(code, chunks[index].Width, chunks[index].Offset);
        }
    }

    private static void BulkCopy(IWasmInstructionWriter code, int destination, int source, int byteCount)
    {
        Get(code, destination);
        Get(code, source);
        Get(code, byteCount);
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.Prefixed,
            WasmInstructionOperand.PrefixedTriple(WasmOpcodes.MemoryCopy, 0, 0)));
    }

    private static void BulkFill(IWasmInstructionWriter code, int destination, int value, int byteCount)
    {
        Get(code, destination);
        Get(code, value);
        Get(code, byteCount);
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.Prefixed,
            WasmInstructionOperand.PrefixedPair(WasmOpcodes.MemoryFill, 0)));
    }

    // if (n > 128) bulk; else if (n >= minimum0) case0; else if ... ; n == 0 stores nothing.
    private static void EmitCascade(
        IWasmInstructionWriter code,
        WasmTargetLayout target,
        int byteCount,
        Action bulk,
        int caseCount,
        Action<int> emitCase,
        Func<int, long> minimum)
    {
        Compare(code, target, byteCount, InlineLimit + 1);
        If(code);
        bulk();
        for (var index = 0; index < caseCount; index++)
        {
            Else(code);
            Compare(code, target, byteCount, minimum(index));
            If(code);
            emitCase(index);
        }
        for (var index = 0; index <= caseCount; index++)
        {
            code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
        }
    }

    // Pushes byteCount >= minimum, unsigned.
    private static void Compare(IWasmInstructionWriter code, WasmTargetLayout target, int byteCount, long minimum)
    {
        Get(code, byteCount);
        Constant(code, target, minimum);
        code.Write(WasmInstruction.NoOperand(
            target.UsesMemory64 ? WasmOpcodes.I64GreaterThanOrEqualUnsigned : WasmOpcodes.I32GreaterThanOrEqualUnsigned));
    }

    // Pushes base + offset, where a negative offset counts back from the end.
    private static void Address(
        IWasmInstructionWriter code,
        WasmTargetLayout target,
        int baseLocal,
        int byteCount,
        long offset)
    {
        Get(code, baseLocal);
        if (offset < 0)
        {
            Get(code, byteCount);
            code.Write(WasmInstruction.NoOperand(target.UsesMemory64 ? WasmOpcodes.I64Add : WasmOpcodes.I32Add));
        }
        if (offset != 0)
        {
            Constant(code, target, offset);
            code.Write(WasmInstruction.NoOperand(target.UsesMemory64 ? WasmOpcodes.I64Add : WasmOpcodes.I32Add));
        }
    }

    // The fill byte repeated across a store of the width (16, 8, 4, 2 or 1); a
    // one-byte store takes the value as it is.
    private static void Repeated(IWasmInstructionWriter code, int value, int width)
    {
        Get(code, value);
        if (width == 16)
        {
            code.Write(WasmInstruction.WithOperand(
                WasmOpcodes.SimdPrefixed,
                WasmInstructionOperand.Prefixed(WasmSimdOpcodes.I8x16Splat)));
        }
        else if (width == 8)
        {
            code.Write(WasmInstruction.WithOperand(WasmOpcodes.I32Constant, WasmInstructionOperand.Signed(0xff)));
            code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32And));
            code.Write(WasmInstruction.NoOperand(WasmOpcodes.I64ExtendI32Unsigned));
            code.Write(WasmInstruction.WithOperand(
                WasmOpcodes.I64Constant,
                WasmInstructionOperand.Signed64(0x0101010101010101)));
            code.Write(WasmInstruction.NoOperand(WasmOpcodes.I64Multiply));
        }
        else if (width > 1)
        {
            code.Write(WasmInstruction.WithOperand(WasmOpcodes.I32Constant, WasmInstructionOperand.Signed(0xff)));
            code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32And));
            code.Write(WasmInstruction.WithOperand(WasmOpcodes.I32Constant, WasmInstructionOperand.Signed(0x01010101)));
            code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32Multiply));
        }
    }

    private static void Load(IWasmInstructionWriter code, int width, uint offset = 0) => code.Write(width switch
    {
        16 => WasmInstruction.WithOperand(
            WasmOpcodes.SimdPrefixed,
            WasmInstructionOperand.PrefixedTriple(WasmSimdOpcodes.V128Load, 0, offset)),
        8 => WasmInstruction.WithOperand(WasmOpcodes.I64Load, WasmInstructionOperand.Memory(0, offset)),
        4 => WasmInstruction.WithOperand(WasmOpcodes.I32Load, WasmInstructionOperand.Memory(0, offset)),
        2 => WasmInstruction.WithOperand(WasmOpcodes.I32Load16Unsigned, WasmInstructionOperand.Memory(0, offset)),
        _ => WasmInstruction.WithOperand(WasmOpcodes.I32Load8Unsigned, WasmInstructionOperand.Memory(0, offset)),
    });

    private static void Store(IWasmInstructionWriter code, int width, uint offset = 0) => code.Write(width switch
    {
        16 => WasmInstruction.WithOperand(
            WasmOpcodes.SimdPrefixed,
            WasmInstructionOperand.PrefixedTriple(WasmSimdOpcodes.V128Store, 0, offset)),
        8 => WasmInstruction.WithOperand(WasmOpcodes.I64Store, WasmInstructionOperand.Memory(0, offset)),
        4 => WasmInstruction.WithOperand(WasmOpcodes.I32Store, WasmInstructionOperand.Memory(0, offset)),
        2 => WasmInstruction.WithOperand(WasmOpcodes.I32Store16, WasmInstructionOperand.Memory(0, offset)),
        _ => WasmInstruction.WithOperand(WasmOpcodes.I32Store8, WasmInstructionOperand.Memory(0, offset)),
    });

    private static void Constant(IWasmInstructionWriter code, WasmTargetLayout target, long value) =>
        code.Write(target.UsesMemory64
            ? WasmInstruction.WithOperand(WasmOpcodes.I64Constant, WasmInstructionOperand.Signed64(value))
            : WasmInstruction.WithOperand(WasmOpcodes.I32Constant, WasmInstructionOperand.Signed((int)value)));

    private static void If(IWasmInstructionWriter code) =>
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.If,
            WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));

    private static void Else(IWasmInstructionWriter code) =>
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.Else));

    private static void Set(IWasmInstructionWriter code, int local) =>
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.LocalSet,
            WasmInstructionOperand.Unsigned((uint)local)));

    private static void Get(IWasmInstructionWriter code, int local) =>
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.LocalGet,
            WasmInstructionOperand.Unsigned((uint)local)));
}
