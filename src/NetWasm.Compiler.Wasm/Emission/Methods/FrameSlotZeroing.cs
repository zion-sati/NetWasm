// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Emission.Methods;

// Zeroes a root frame just entered, apart from the slots the code right after the entry writes anyway (an authoritative
// variable's copy, the stores of the publication that entered it), with no safepoint in between: the fewest stores that
// cover every other slot, each free to cover a written slot too but none reaching past the frame, and of those the
// fewest bytes. With no slot written, or a frame large enough to be filled, it zeroes the whole range as
// FrameRangeZeroing does.
internal static class FrameSlotZeroing
{
    private static readonly int[] Widths = [16, 8, 4];

    public static void Emit(
        IWasmInstructionWriter code,
        WasmTargetLayout target,
        Action<IWasmInstructionWriter> start,
        int slotCount,
        IReadOnlySet<int> written)
    {
        ArgumentNullException.ThrowIfNull(code);
        ArgumentNullException.ThrowIfNull(start);
        ArgumentNullException.ThrowIfNull(written);
        if (Plan(target, slotCount, written) is not { } stores)
        {
            FrameRangeZeroing.Emit(code, target, start, checked(slotCount * target.ObjectReferenceSize));
            return;
        }
        foreach (var (offset, width) in stores) FrameRangeZeroing.EmitStore(code, target, start, offset, width);
    }

    // The instructions that store or fill: one per store, or the fill.
    public static int Writes(WasmTargetLayout target, int slotCount, IReadOnlySet<int> written)
    {
        ArgumentNullException.ThrowIfNull(written);
        return Plan(target, slotCount, written) is { } stores
            ? stores.Length
            : FrameRangeZeroing.Writes(checked(slotCount * target.ObjectReferenceSize));
    }

    // The stores, as (byte offset, width), or null where the whole range is zeroed.
    private static ImmutableArray<(int Offset, int Width)>? Plan(WasmTargetLayout target, int slotCount, IReadOnlySet<int> written)
    {
        var slot = target.ObjectReferenceSize;
        if (checked(slotCount * slot) > FrameRangeZeroing.MaximumStoredBytes ||
            !written.Any(index => index >= 0 && index < slotCount))
        {
            return null;
        }
        // From the frame's end back: the cheapest way to zero what must be zero from each slot on, and the width of the
        // store that starts there (0 where none does).
        var best = new (int Stores, int Bytes, int Width)[slotCount + 1];
        for (var index = slotCount - 1; index >= 0; index--)
        {
            var choice = written.Contains(index)
                ? (best[index + 1].Stores, best[index + 1].Bytes, Width: 0)
                : (Stores: int.MaxValue, Bytes: int.MaxValue, Width: 0);
            foreach (var width in Widths)
            {
                var next = index + width / slot;
                if (width < slot || next > slotCount) continue;
                var candidate = (Stores: best[next].Stores + 1, Bytes: best[next].Bytes + CodeBytes(width), Width: width);
                if (candidate.Stores < choice.Stores || candidate.Stores == choice.Stores && candidate.Bytes < choice.Bytes)
                {
                    choice = candidate;
                }
            }
            best[index] = choice;
        }
        var stores = ImmutableArray.CreateBuilder<(int Offset, int Width)>();
        for (var index = 0; index < slotCount;)
        {
            if (best[index].Width is var width and > 0)
            {
                stores.Add((index * slot, width));
                index += width / slot;
            }
            else
            {
                index++;
            }
        }
        return stores.ToImmutable();
    }

    // A store's size in the function body: a v128 zero's constant is 16 bytes of its own.
    private static int CodeBytes(int width) => width == 16 ? 24 : 7;
}
