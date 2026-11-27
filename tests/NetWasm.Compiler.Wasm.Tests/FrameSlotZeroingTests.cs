// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission.Methods;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Tests;

using static EmitterTestSupport;

// Zeroing a root frame just entered, around the slots the code right after the entry writes: every other slot is
// zeroed, no store reaches past the frame, and never more stores than zeroing the whole range; with nothing written, or a
// frame large enough to fill, it is the whole range's zeroing.
public sealed class FrameSlotZeroingTests
{
    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void ZeroesEverySlotNotWrittenInsideTheFrameWithNoMoreStoresThanTheWholeRange(WasmTarget target)
    {
        var layout = WasmTargetLayout.For(target);
        var slot = layout.ObjectReferenceSize;
        for (var slots = 1; slots <= 12; slots++)
        {
            for (var mask = 0; mask < 1 << slots; mask++)
            {
                var written = Enumerable.Range(0, slots).Where(index => (mask >> index & 1) != 0).ToHashSet();
                var stores = Stores(layout, slots, written);
                var covered = new bool[slots * slot];
                foreach (var (offset, width) in stores)
                {
                    Assert.True(offset >= 0 && offset + width <= slots * slot, $"{slots} slots, mask {mask}: a store past the frame");
                    for (var at = offset; at < offset + width; at++) covered[at] = true;
                }
                for (var index = 0; index < slots; index++)
                {
                    if (written.Contains(index)) continue;
                    Assert.True(Enumerable.Range(index * slot, slot).All(at => covered[at]), $"{slots} slots, mask {mask}: slot {index} not zeroed");
                }
                Assert.Equal(stores.Count, FrameSlotZeroing.Writes(layout, slots, written));
                Assert.True(stores.Count <= FrameRangeZeroing.Writes(slots * slot), $"{slots} slots, mask {mask}: more stores than the range");
            }
        }
    }

    // The publication that entered a three-slot frame stores two of its slots: one store zeroes the third.
    [Fact]
    public void ZeroesOnlyTheSlotThePublicationLeaves()
    {
        Assert.Equal([(8, 4)], Stores(WasmTargetLayout.For(WasmTarget.Wasm32), 3, new HashSet<int> { 0, 1 }));
        Assert.Equal([(8, 16)], Stores(WasmTargetLayout.For(WasmTarget.Wasm64), 3, new HashSet<int> { 0 }));
    }

    [Theory]
    [InlineData(WasmTarget.Wasm32, 7, false)]
    [InlineData(WasmTarget.Wasm64, 5, false)]
    [InlineData(WasmTarget.Wasm32, 100, false)]
    [InlineData(WasmTarget.Wasm32, 100, true)]
    [InlineData(WasmTarget.Wasm64, 40, true)]
    public void ZeroesTheWholeRangeWithNothingWrittenOrAFrameToFill(WasmTarget target, int slots, bool written)
    {
        var layout = WasmTargetLayout.For(target);
        var expected = new RecordingInstructionWriter();
        FrameRangeZeroing.Emit(expected, layout, Start, slots * layout.ObjectReferenceSize);
        var code = new RecordingInstructionWriter();

        FrameSlotZeroing.Emit(code, layout, Start, slots, written ? new HashSet<int> { 0, 3 } : new HashSet<int>());

        // A v128 constant's operand holds its bytes by reference, so instructions compare by their text.
        Assert.Equal(expected.ToInstructions().Select(static instruction => instruction.ToString()),
            code.ToInstructions().Select(static instruction => instruction.ToString()));
    }

    [Fact]
    public void RejectsMissingArguments()
    {
        var layout = WasmTargetLayout.For(WasmTarget.Wasm32);
        Assert.Throws<ArgumentNullException>(() => FrameSlotZeroing.Emit(null!, layout, Start, 1, new HashSet<int>()));
        Assert.Throws<ArgumentNullException>(() => FrameSlotZeroing.Emit(new RecordingInstructionWriter(), layout, null!, 1, new HashSet<int>()));
        Assert.Throws<ArgumentNullException>(() => FrameSlotZeroing.Emit(new RecordingInstructionWriter(), layout, Start, 1, null!));
        Assert.Throws<ArgumentNullException>(() => FrameSlotZeroing.Writes(layout, 1, null!));
    }

    private static void Start(IWasmInstructionWriter writer) =>
        writer.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned(0)));

    // The (offset, width) of each store the zeroing emits.
    private static List<(int Offset, int Width)> Stores(WasmTargetLayout layout, int slots, IReadOnlySet<int> written)
    {
        var code = new RecordingInstructionWriter();
        FrameSlotZeroing.Emit(code, layout, Start, slots, written);
        var stores = new List<(int Offset, int Width)>();
        foreach (var instruction in code.ToInstructions())
        {
            if (instruction.Opcode == WasmOpcodes.SimdPrefixed && instruction.Operand.Alignment == WasmSimdOpcodes.V128Store)
                stores.Add(((int)instruction.Operand.Third, 16));
            else if (instruction.Opcode == WasmOpcodes.I64Store) stores.Add(((int)instruction.Operand.Offset, 8));
            else if (instruction.Opcode == WasmOpcodes.I32Store) stores.Add(((int)instruction.Operand.Offset, 4));
        }
        return stores;
    }
}
