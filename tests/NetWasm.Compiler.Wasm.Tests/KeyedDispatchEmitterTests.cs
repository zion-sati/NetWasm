// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Immutable;
using NetWasm.Compiler.Wasm.Emission.Instructions.Dispatch;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Tests;

// The emitted search runs each key's case, or the fallback for a key no range holds, and nests no deeper than its
// halvings: an if per range made code that grows with the program, past the stack of a tool that recurses per if.
public sealed class KeyedDispatchEmitterTests
{
    private const int CaseMarker = 1_000_000;
    private const int FallbackMarker = -1;

    [Fact]
    public void RejectsMissingWritersAndDispatches()
    {
        var emitter = new KeyedDispatchEmitter();
        Assert.Throws<ArgumentNullException>(() => emitter.Emit(null!, Dispatch(new List<WasmInstruction>(), false, [new(1, 1)])));
        Assert.Throws<ArgumentNullException>(() => emitter.Emit(new Writer(), null!));
    }

    // Fewer ranges than the search needs are tested in turn: an equality for a single key, a distance for a run.
    [Fact]
    public void AFewRangesAreTestedInTurn()
    {
        var writer = new Writer();
        new KeyedDispatchEmitter().Emit(writer, Dispatch(writer.Code, false, [new(3, 3), new(5, 11)]));
        Assert.Equal(
            [
                WasmOpcodes.LocalGet, WasmOpcodes.I32Constant, WasmOpcodes.I32Equal, WasmOpcodes.If,
                WasmOpcodes.I32Constant, WasmOpcodes.Drop, WasmOpcodes.Else,
                WasmOpcodes.LocalGet, WasmOpcodes.I32Constant, WasmOpcodes.I32Subtract, WasmOpcodes.I32Constant,
                WasmOpcodes.I32LessThanUnsigned, WasmOpcodes.If,
                WasmOpcodes.I32Constant, WasmOpcodes.Drop, WasmOpcodes.Else,
                WasmOpcodes.Unreachable, WasmOpcodes.End, WasmOpcodes.End,
            ],
            writer.Code.Select(instruction => instruction.Opcode));
        // The run 5 through 11 is the key less 5, below 7.
        Assert.Equal(5, writer.Code[8].Operand.SignedValue);
        Assert.Equal(7, writer.Code[10].Operand.SignedValue);
    }

    public static TheoryData<bool, int> Sizes => new()
    {
        { false, 1 }, { false, 3 }, { false, 4 }, { false, 5 }, { false, 17 }, { false, 2_600 },
        { true, 1 }, { true, 4 }, { true, 2_600 },
    };

    // Every key, and those just outside each range, finds its case; the nesting grows with the halvings.
    [Theory]
    [MemberData(nameof(Sizes))]
    public void EachKeyRunsItsCase(bool wide, int count)
    {
        // Single keys and runs alternate with gaps between them; wide keys sit above 32 bits.
        var origin = wide ? 0x1_0000_0000L : 7L;
        var ranges = Enumerable.Range(0, count)
            .Select(index => new KeyRange(origin + index * 5L, origin + index * 5L + (index % 3 == 0 ? 2 : 0)))
            .ToImmutableArray();
        var writer = new Writer();
        new KeyedDispatchEmitter().Emit(writer, Dispatch(writer.Code, wide, ranges));

        var interpreter = new Interpreter(writer.Code, wide);
        for (var index = 0; index < ranges.Length; index++)
        {
            var range = ranges[index];
            Assert.Equal(index, interpreter.Run(range.Low));
            Assert.Equal(index, interpreter.Run(range.High));
            Assert.Equal(FallbackMarker, interpreter.Run(range.Low - 1));
            Assert.Equal(FallbackMarker, interpreter.Run(range.High + 1));
        }
        Assert.Equal(FallbackMarker, interpreter.Run(0));
        var halvings = (int)Math.Ceiling(Math.Log2(Math.Max(count, 1)));
        Assert.InRange(interpreter.MaximumDepth, 1, count < KeyedDispatchEmitter.MinimumSearchedRanges ? count : halvings + 3);
    }

    // Membership merges runs of consecutive keys into one range each, as unsigned 32-bit keys, and accepts exactly the
    // keys it was given, repeated or not.
    [Fact]
    public void MembershipAcceptsExactlyItsKeysAsRuns()
    {
        int[] keys = [12, 11, -1, 40, 13, 41, 11, 7, 100, 102];
        var writer = new Writer();
        var dispatch = KeyedDispatch.Membership(4, keys,
            whenIn: () =>
            {
                writer.Code.Add(WasmInstruction.WithOperand(WasmOpcodes.I32Constant, WasmInstructionOperand.Signed(CaseMarker)));
                writer.Code.Add(WasmInstruction.NoOperand(WasmOpcodes.Drop));
            },
            whenOut: () => writer.Code.Add(WasmInstruction.NoOperand(WasmOpcodes.Unreachable)));

        Assert.False(dispatch.Wide);
        Assert.Equal(
            [new(7, 7), new(11, 13), new(40, 41), new(100, 100), new(102, 102), new(uint.MaxValue, uint.MaxValue)],
            dispatch.Ranges);
        new KeyedDispatchEmitter().Emit(writer, dispatch);
        var interpreter = new Interpreter(writer.Code, wide: false);
        for (var key = 0L; key < 110; key++)
        {
            Assert.Equal(keys.Contains((int)key) ? 0 : FallbackMarker, interpreter.Run(key));
        }
        Assert.Equal(0, interpreter.Run(uint.MaxValue));
        Assert.Empty(KeyedDispatch.Membership(4, [], () => { }, () => { }).Ranges);
        Assert.Throws<ArgumentNullException>(() => KeyedDispatch.Membership(4, null!, () => { }, () => { }));
    }

    // Of orders its cases by key as unsigned values and keeps a repeated key's first case, the one a test of each key
    // in turn reaches first; a narrow key is its 32 bits.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OfOrdersCasesAndKeepsARepeatedKeysFirst(bool wide)
    {
        var writer = new Writer();
        var reached = new List<string>();
        (long Key, string Name)[] cases = [(9, "nine"), (-2, "minus two"), (3, "three"), (9, "nine again"), (0x1_0000_0001, "wide")];
        var dispatch = KeyedDispatch.Of(4, wide,
            cases.Select(item => (item.Key, (Action)(() =>
            {
                writer.Code.Add(WasmInstruction.WithOperand(WasmOpcodes.I32Constant,
                    WasmInstructionOperand.Signed(CaseMarker + reached.Count)));
                writer.Code.Add(WasmInstruction.NoOperand(WasmOpcodes.Drop));
                reached.Add(item.Name);
            }))),
            () => writer.Code.Add(WasmInstruction.NoOperand(WasmOpcodes.Unreachable)));

        var expected = wide
            ? new long[] { 3, 9, 0x1_0000_0001, -2 }
            : [1, 3, 9, uint.MaxValue - 1];
        Assert.Equal(expected, dispatch.Ranges.Select(range => range.Low));
        new KeyedDispatchEmitter().Emit(writer, dispatch);
        Assert.Equal(wide ? ["three", "nine", "wide", "minus two"] : ["wide", "three", "nine", "minus two"], reached);
        var interpreter = new Interpreter(writer.Code, wide);
        Assert.Equal(wide ? 1 : 2, interpreter.Run(9));
        Assert.Equal(FallbackMarker, interpreter.Run(4));
        Assert.Throws<ArgumentNullException>(() => KeyedDispatch.Of(4, wide, null!, () => { }));
    }

    // A dispatch whose key no local holds reads it again for each test.
    [Fact]
    public void LoadKeyReplacesEveryReadOfTheKeyLocal()
    {
        var writer = new Writer();
        var loads = 0;
        var dispatch = Dispatch(writer.Code, false, [new(1, 1), new(3, 4), new(8, 8), new(10, 10), new(20, 30)],
            code =>
            {
                loads++;
                code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned(4)));
            });

        new KeyedDispatchEmitter().Emit(writer, dispatch);

        Assert.Equal(writer.Code.Count(instruction => instruction.Opcode == WasmOpcodes.LocalGet), loads);
        var interpreter = new Interpreter(writer.Code, wide: false);
        Assert.Equal(1, interpreter.Run(3));
        Assert.Equal(4, interpreter.Run(25));
        Assert.Equal(FallbackMarker, interpreter.Run(9));
    }

    private static KeyedDispatch Dispatch(List<WasmInstruction> code, bool wide, ImmutableArray<KeyRange> ranges,
        Action<IWasmInstructionWriter>? loadKey = null) => new(
        keyLocal: 4,
        wide,
        ranges,
        index =>
        {
            code.Add(WasmInstruction.WithOperand(WasmOpcodes.I32Constant, WasmInstructionOperand.Signed(CaseMarker + index)));
            code.Add(WasmInstruction.NoOperand(WasmOpcodes.Drop));
        },
        () => code.Add(WasmInstruction.NoOperand(WasmOpcodes.Unreachable)))
        {
            LoadKey = loadKey,
        };

    private sealed class Writer : IWasmInstructionWriter
    {
        public List<WasmInstruction> Code { get; } = [];

        public void Write(WasmInstruction instruction) => Code.Add(instruction);
    }

    // Runs the dispatch for a key held in local 4 and reports the case it reached, or the fallback.
    private sealed class Interpreter
    {
        private readonly List<WasmInstruction> _code;
        private readonly bool _wide;
        private readonly Dictionary<int, int> _else = [];
        private readonly Dictionary<int, int> _end = [];

        public Interpreter(List<WasmInstruction> code, bool wide)
        {
            _code = code;
            _wide = wide;
            var open = new Stack<int>();
            var depth = 0;
            for (var position = 0; position < code.Count; position++)
            {
                switch (code[position].Opcode)
                {
                    case WasmOpcodes.Block or WasmOpcodes.If:
                        open.Push(position);
                        MaximumDepth = Math.Max(MaximumDepth, ++depth);
                        break;
                    case WasmOpcodes.Else:
                        _else[open.Peek()] = position;
                        break;
                    case WasmOpcodes.End:
                        _end[open.Pop()] = position;
                        depth--;
                        break;
                }
            }
            Assert.Empty(open);
        }

        public int MaximumDepth { get; }

        public int Run(long key)
        {
            var stack = new Stack<long>();
            // Each open construct's end, where a branch to it continues.
            var labels = new Stack<int>();
            var position = 0;
            while (position < _code.Count)
            {
                var instruction = _code[position];
                switch (instruction.Opcode)
                {
                    // A narrow key and its constants are 32-bit values, held here as their unsigned bits.
                    case WasmOpcodes.LocalGet:
                        Assert.Equal(4u, instruction.Operand.UnsignedValue);
                        stack.Push(_wide ? key : unchecked((uint)key));
                        break;
                    case WasmOpcodes.I32Constant:
                        Assert.False(_wide && instruction.Operand.SignedValue < CaseMarker);
                        if (instruction.Operand.SignedValue >= CaseMarker) return instruction.Operand.SignedValue - CaseMarker;
                        stack.Push(unchecked((uint)instruction.Operand.SignedValue));
                        break;
                    case WasmOpcodes.I64Constant:
                        Assert.True(_wide);
                        stack.Push(instruction.Operand.Signed64Value);
                        break;
                    case WasmOpcodes.I32Equal or WasmOpcodes.I64Equal:
                        Assert.Equal(_wide, instruction.Opcode == WasmOpcodes.I64Equal);
                        stack.Push(stack.Pop() == stack.Pop() ? 1 : 0);
                        break;
                    case WasmOpcodes.I32Subtract or WasmOpcodes.I64Subtract:
                        {
                            var right = stack.Pop();
                            var difference = stack.Pop() - right;
                            stack.Push(instruction.Opcode == WasmOpcodes.I32Subtract ? unchecked((uint)difference) : difference);
                            break;
                        }
                    case WasmOpcodes.I32LessThanUnsigned or WasmOpcodes.I64LessThanUnsigned:
                        {
                            var right = (ulong)stack.Pop();
                            stack.Push((ulong)stack.Pop() < right ? 1 : 0);
                            break;
                        }
                    case WasmOpcodes.Block:
                        labels.Push(_end[position]);
                        break;
                    case WasmOpcodes.If:
                        labels.Push(_end[position]);
                        if (stack.Pop() == 0)
                        {
                            if (_else.TryGetValue(position, out var otherwise))
                            {
                                position = otherwise;
                            }
                            else
                            {
                                position = _end[position];
                                labels.Pop();
                            }
                        }
                        break;
                    case WasmOpcodes.Else:
                        // The end of a taken arm skips the other.
                        position = labels.Pop();
                        break;
                    case WasmOpcodes.End:
                        labels.Pop();
                        break;
                    case WasmOpcodes.Branch:
                        for (var unwound = 0; unwound < instruction.Operand.UnsignedValue; unwound++) labels.Pop();
                        position = labels.Pop();
                        break;
                    case WasmOpcodes.Unreachable:
                        return FallbackMarker;
                    default:
                        throw new InvalidOperationException($"Unexpected opcode 0x{instruction.Opcode:x2}.");
                }
                position++;
            }
            throw new InvalidOperationException("The dispatch ran off its end without a case or the fallback.");
        }
    }
}
