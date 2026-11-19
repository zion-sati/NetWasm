// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

namespace NetWasm.Compiler.Tests.Correctness;

// A field read after a store through the same local or argument takes the stored value. Each shape here gives that
// read a reason to differ from the store: another slot naming the same object stores in between, the slot changes
// before the store takes its object, the field narrows what is stored, a call or a ref writes it, a branch skips
// the store, or a static's initializer runs at the store. Even inputs make both nodes one object.
public sealed class FieldStoreForwardingCompilationTests(CorrectnessTestRunner runner)
{
    [Fact]
    public void ReadsAfterStoresSeeWhatTheFieldHolds() =>
        runner.Run(new("FieldStoreForwarding", "FieldStoreForwarding", Source, [0, 1, 2, 3, 7])
        {
            ReportAllMismatches = true,
        });

    private const string Source = """
        using System;
        using System.Runtime.CompilerServices;
        namespace FieldStoreForwarding;

        public sealed class Node
        {
            public int X;
            public long L;
            public nint P;
            public uint U;
            public byte B;
            public short H;
            public Node? Next;
            public string S = "";
        }

        public struct Pair
        {
            public int X;
            public int Y;

            public void Both(ref Pair other)
            {
                X = 1;
                other.X = 2;
                Y = X + 10;
            }
        }

        public static class Statics
        {
            public static int S;
            public static long L;
        }

        public static class Lazy
        {
            public static int Value = Init();

            private static int Init()
            {
                EntryPoint.Log(7);
                return 3;
            }
        }

        public sealed class Writer
        {
            private byte[] buffer = new byte[2];
            private int length;

            public int Length => length;

            public int Sum()
            {
                var sum = 0;
                for (var i = 0; i < length; i++) sum = sum * 3 + buffer[i];
                return sum;
            }

            public void Int(int value)
            {
                if (length + 4 > buffer.Length) Grow(4);
                buffer[length++] = (byte)value;
                buffer[length++] = (byte)(value >> 8);
                buffer[length++] = (byte)(value >> 16);
                buffer[length++] = (byte)(value >> 24);
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private void Grow(int count)
            {
                var next = new byte[Math.Max(buffer.Length * 2, length + count)];
                buffer.AsSpan(0, length).CopyTo(next);
                buffer = next;
            }
        }

        public static class EntryPoint
        {
            private static int _trace;

            public static int Trace() => _trace;

            internal static void Log(int value) => _trace = unchecked(_trace * 31 + value);

            public static int Run(int input)
            {
                var a = new Node();
                var b = input % 2 == 0 ? a : new Node();
                var hash = 17;
                hash = Mix(hash, Alias(a, b, input));
                hash = Mix(hash, Wide(a, input));
                hash = Mix(hash, References(a, b, input));
                hash = Mix(hash, SlotChanges(a, b, input));
                hash = Mix(hash, Increments(a, input));
                hash = Mix(hash, StaticStores(input));
                hash = Mix(hash, Narrow(a, input));
                hash = Mix(hash, Branches(a, b, input));
                hash = Mix(hash, Loop(a, input));
                hash = Mix(hash, Calls(a, input));
                hash = Mix(hash, Refs(a, input));
                hash = Mix(hash, Structs(input));
                hash = Mix(hash, Writes(input));
                hash = Mix(hash, Handlers(a, input));
                _trace = Mix(_trace, hash);
                return hash;
            }

            private static int Mix(int hash, int value) => unchecked(hash * 31 + value);

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int Alias(Node a, Node b, int input)
            {
                a.X = input;
                b.X = input + 5;
                var first = a.X;
                a.X = first * 2;
                return first * 3 + b.X;
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int Wide(Node a, int input)
            {
                a.L = (long)input << 40;
                a.P = (nint)input * 3;
                a.U = uint.MaxValue - (uint)input;
                return (int)(a.L >> 38) + (int)a.P + (int)(a.U >> 4);
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int References(Node a, Node b, int input)
            {
                a.Next = b;
                a.S = input.ToString();
                b.Next = null;
                return (a.Next == null ? 5 : 1) + (a.Next == b ? 2 : 0) + a.S.Length;
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int SlotChanges(Node a, Node b, int input)
            {
                b.X = 40;
                a.X = Id(a = b, input);
                return a.X;
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int Id(Node node, int value) => value + 100;

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int Increments(Node a, int input)
            {
                a.X = input;
                a.X++;
                a.X += 3;
                var x = a.X;
                a.L = input;
                a.L <<= 33;
                return x + (int)(a.L >> 30);
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int StaticStores(int input)
            {
                Statics.S = input;
                Statics.S *= 2;
                var result = Statics.S;
                Statics.L = -input;
                result += (int)Statics.L;
                Lazy.Value = input + 1;
                return result * 7 + Lazy.Value;
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int Narrow(Node a, int input)
            {
                a.B = (byte)(input + 250);
                a.H = (short)(input * 20000);
                return a.B * 3 + a.H;
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int Branches(Node a, Node b, int input)
            {
                a.X = 9;
                if (input > 2) a.X = 1;
                else b.X = 2;
                return a.X;
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int Loop(Node a, int input)
            {
                a.X = 0;
                for (var i = 0; i <= input; i++) a.X = a.X * 3 + i;
                return a.X;
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int Calls(Node a, int input)
            {
                a.X = input;
                Bump(a);
                return a.X;
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static void Bump(Node node) => node.X += 1000;

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int Refs(Node a, int input)
            {
                ref var x = ref a.X;
                a.X = input;
                x = input + 50;
                return a.X;
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int Structs(int input)
            {
                var pair = new Pair();
                pair.Both(ref pair);
                var other = new Pair();
                var first = new Pair();
                first.Both(ref other);
                return pair.Y * 100 + first.Y + other.X * input;
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int Writes(int input)
            {
                var writer = new Writer();
                for (var i = 0; i <= input; i++) writer.Int(i * 0x01020304 + input);
                return writer.Length * 1000 + writer.Sum();
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int Handlers(Node a, int input)
            {
                try
                {
                    a.X = input;
                    if (input > 1) throw new InvalidOperationException();
                    a.X = 3;
                }
                catch (InvalidOperationException)
                {
                    a.X += 20;
                }
                return a.X;
            }
        }
        """;
}
