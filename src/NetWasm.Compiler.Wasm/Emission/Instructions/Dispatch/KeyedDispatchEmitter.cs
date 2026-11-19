// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Emission.Instructions.Dispatch;

// The keys Low through High, which one case serves.
internal readonly record struct KeyRange(long Low, long High);

// A choice among cases by a key a local holds: a type id, a function's address, a descriptor's or an enum value. Keys
// compare unsigned, so ranges are disjoint and ordered by their keys as unsigned values; a narrow key's constants are its
// 32 bits. EmitCase(index) writes the code of the case at that index, and EmitFallback the code for a key no range
// holds. Wide keys are 64-bit, as a wasm64 address or a long enum's value is.
internal sealed class KeyedDispatch(
    int keyLocal,
    bool wide,
    ImmutableArray<KeyRange> ranges,
    Action<int> emitCase,
    Action emitFallback)
{
    public int KeyLocal { get; } = keyLocal;

    public bool Wide { get; } = wide;

    public ImmutableArray<KeyRange> Ranges { get; } = ranges;

    public Action<int> EmitCase { get; } = emitCase;

    public Action EmitFallback { get; } = emitFallback;

    // Writes the key where no local holds it, as an object's type id read from its header; each test reads it again.
    public Action<IWasmInstructionWriter>? LoadKey { get; init; }

    // Whether the key is one of the given keys: one case, for the runs of consecutive keys, and the fallback for the
    // rest. LoadKey, when given, writes the key where no local holds it.
    public static KeyedDispatch Membership(int keyLocal, IEnumerable<int> keys, Action whenIn, Action whenOut,
        Action<IWasmInstructionWriter>? loadKey = null)
    {
        ArgumentNullException.ThrowIfNull(keys);
        var ranges = new List<KeyRange>();
        foreach (var key in keys.Select(key => (long)unchecked((uint)key)).Distinct().Order())
        {
            if (ranges.Count != 0 && ranges[^1].High + 1 == key)
                ranges[^1] = ranges[^1] with { High = key };
            else
                ranges.Add(new(key, key));
        }
        return new(keyLocal, wide: false, [.. ranges], _ => whenIn(), whenOut) { LoadKey = loadKey };
    }

    // A case per key, as a test of each key in turn writes them: ordered by key, and a repeated key's first case, which
    // a test in turn reaches first, kept.
    public static KeyedDispatch Of(int keyLocal, bool wide, IEnumerable<(long Key, Action Emit)> cases, Action fallback)
    {
        ArgumentNullException.ThrowIfNull(cases);
        var ordered = cases
            .Select(item => (Key: wide ? item.Key : (long)unchecked((uint)item.Key), item.Emit))
            .DistinctBy(item => item.Key)
            .OrderBy(item => unchecked((ulong)item.Key))
            .ToArray();
        return new(keyLocal, wide, [.. ordered.Select(item => new KeyRange(item.Key, item.Key))],
            index => ordered[index].Emit(), fallback);
    }
}

internal interface IKeyedDispatchEmitter
{
    void Emit(IWasmInstructionWriter code, KeyedDispatch dispatch);
}

// A few ranges are tested in turn. More are searched, halving them at each branch, so a key takes a branch per halving
// and one range test, and the code nests as deep as the halvings: a test of each range in turn nests an if per range,
// which grows with the program and runs past the stack of a tool that recurses on each nested if.
internal sealed class KeyedDispatchEmitter : IKeyedDispatchEmitter
{
    internal const int MinimumSearchedRanges = 4;

    public void Emit(IWasmInstructionWriter code, KeyedDispatch dispatch)
    {
        ArgumentNullException.ThrowIfNull(code);
        ArgumentNullException.ThrowIfNull(dispatch);
        if (dispatch.Ranges.Length < MinimumSearchedRanges)
        {
            EmitInTurn(code, dispatch);
            return;
        }
        // The outer block is left once a case has run; the inner one when no range holds the key, for the fallback.
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.Block, WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.Block, WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
        EmitSearch(code, dispatch, 0, dispatch.Ranges.Length, 0);
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
        dispatch.EmitFallback();
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
    }

    private static void EmitInTurn(IWasmInstructionWriter code, KeyedDispatch dispatch)
    {
        for (var index = 0; index < dispatch.Ranges.Length; index++)
        {
            EmitRangeTest(code, dispatch, dispatch.Ranges[index]);
            code.Write(WasmInstruction.WithOperand(WasmOpcodes.If, WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
            dispatch.EmitCase(index);
            code.Write(WasmInstruction.NoOperand(WasmOpcodes.Else));
        }
        dispatch.EmitFallback();
        for (var index = 0; index < dispatch.Ranges.Length; index++)
        {
            code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
        }
    }

    // Ranges start through start + count - 1 within enclosingIfDepth ifs of the search.
    private static void EmitSearch(IWasmInstructionWriter code, KeyedDispatch dispatch, int start, int count, int enclosingIfDepth)
    {
        if (count == 1)
        {
            EmitRangeTest(code, dispatch, dispatch.Ranges[start]);
            code.Write(WasmInstruction.WithOperand(WasmOpcodes.If, WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
            dispatch.EmitCase(start);
            code.Write(WasmInstruction.WithOperand(WasmOpcodes.Branch,
                WasmInstructionOperand.Unsigned(checked((uint)(enclosingIfDepth + 2)))));
            code.Write(WasmInstruction.NoOperand(WasmOpcodes.Else));
            code.Write(WasmInstruction.WithOperand(WasmOpcodes.Branch,
                WasmInstructionOperand.Unsigned(checked((uint)(enclosingIfDepth + 1)))));
            code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
            return;
        }
        var leftCount = count / 2;
        var pivot = start + leftCount;
        EmitComparison(code, dispatch, dispatch.Ranges[pivot].Low, LessThanUnsigned(dispatch));
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.If, WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
        EmitSearch(code, dispatch, start, leftCount, enclosingIfDepth + 1);
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.Else));
        EmitSearch(code, dispatch, pivot, count - leftCount, enclosingIfDepth + 1);
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
    }

    // Whether the key is in the range: one equality, or one unsigned comparison of its distance from the range's
    // start, which wraps for keys below it.
    private static void EmitRangeTest(IWasmInstructionWriter code, KeyedDispatch dispatch, KeyRange range)
    {
        if (range.Low == range.High)
        {
            EmitComparison(code, dispatch, range.Low, dispatch.Wide ? WasmOpcodes.I64Equal : WasmOpcodes.I32Equal);
            return;
        }
        EmitComparison(code, dispatch, range.Low, dispatch.Wide ? WasmOpcodes.I64Subtract : WasmOpcodes.I32Subtract);
        EmitConstant(code, dispatch, unchecked(range.High - range.Low + 1));
        code.Write(WasmInstruction.NoOperand(LessThanUnsigned(dispatch)));
    }

    private static void EmitComparison(IWasmInstructionWriter code, KeyedDispatch dispatch, long value, byte operation)
    {
        if (dispatch.LoadKey is { } load)
            load(code);
        else
            code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)dispatch.KeyLocal)));
        EmitConstant(code, dispatch, value);
        code.Write(WasmInstruction.NoOperand(operation));
    }

    private static void EmitConstant(IWasmInstructionWriter code, KeyedDispatch dispatch, long value) =>
        code.Write(dispatch.Wide
            ? WasmInstruction.WithOperand(WasmOpcodes.I64Constant, WasmInstructionOperand.Signed64(value))
            : WasmInstruction.WithOperand(WasmOpcodes.I32Constant, WasmInstructionOperand.Signed(unchecked((int)value))));

    private static byte LessThanUnsigned(KeyedDispatch dispatch) =>
        dispatch.Wide ? WasmOpcodes.I64LessThanUnsigned : WasmOpcodes.I32LessThanUnsigned;
}
