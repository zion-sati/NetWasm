// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.ComponentModel;

namespace NetWasm.Compiler.Tests.Correctness;

// A struct of at most four scalar fields is returned in results and passed by its fields; a callee reads such an
// argument in place from its parameters where it only reads its fields, forwards it whole to a call or returns it, and
// keeps a frame copy otherwise; struct locals, call results and temporaries built for a call stay in lanes. Against
// desktop .NET: arguments through static, instance, virtual, interface, delegate and generic calls and constructors,
// one past the field limit, nested ones, a callee writing its copy and one taking a reference; reference fields of
// arguments read in place after allocations, so collections meet them; values forwarded and returned whole; lanes
// carrying negative short and sbyte fields out of a method with several returns inside a lock, and a value task
// whose source's token passes 32,767 on the way; spans; MemoryStream's asynchronous reads and writes; a GetType()
// guard; a virtual call that only a null receiver can reach; and an exception filter reading a struct argument and a
// call's struct result. A second program, kept apart so neither changes what the other reaches, covers calls the
// compiler expands in place or generates: a tuple Math.DivRem deconstructed into a property of an explicit-layout
// struct reached by reference, as decimal division does; tuples of wide integers, one built through its fields' fields,
// and decimal arithmetic; expression
// trees and DynamicInvoke passing and returning structs; a multicast delegate of a struct with reference fields; and an
// array's interface methods taking structs.
public sealed class StructConventionDifferentialTests(CorrectnessTestRunner runner)
{
    public static TheoryData<FinalWasmOptimization, string> Cases()
    {
        var cases = new TheoryData<FinalWasmOptimization, string>();
        foreach (var optimization in (FinalWasmOptimization[])[FinalWasmOptimization.None, FinalWasmOptimization.O1,
                     FinalWasmOptimization.O3, FinalWasmOptimization.Oz])
        {
            cases.Add(optimization, "Release-Wasm32-Direct-Linked");
            cases.Add(optimization, "Debug-Wasm32-Direct-Linked");
        }
        // References and addresses are 64-bit there, so the fields' parameters and lanes are too.
        cases.Add(FinalWasmOptimization.None, "Release-Wasm64-Direct-Linked");
        cases.Add(FinalWasmOptimization.O3, "Release-Wasm64-Direct-Linked");
        return cases;
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void StructConventionsMatchDesktop(FinalWasmOptimization optimization, string cell) =>
        runner.Run(new CorpusFixture(
            "StructConventions", "NetWasm.Correctness.StructConventions", Source, [0, 1, 2, 3, 4, 5])
        {
            CaseId = "values.struct-conventions",
            Matrix = new(CorpusMatrixProfile.Extended, cell, CorpusExecutionBackend.Linked),
            OracleMode = OracleMode.SameSource,
            SameSourceReason = "Compare NetWasm's struct argument and result conventions with desktop .NET's values.",
            Optimization = optimization,
        });

    [Theory]
    [MemberData(nameof(Cases))]
    public void GeneratedCallersAndExpandedCallsMatchDesktop(FinalWasmOptimization optimization, string cell) =>
        runner.Run(new CorpusFixture(
            "StructConventionCallers", "NetWasm.Correctness.StructConventionCallers", CallersSource, [0, 1, 2, 3])
        {
            CaseId = "values.struct-convention-callers",
            Matrix = new(CorpusMatrixProfile.Extended, cell, CorpusExecutionBackend.Linked),
            // DynamicInvoke and the delegates it reaches bring in the platform's event loop, as for DelegateCompatibilityTests.
            RequiresReactor = true,
            OracleMode = OracleMode.SameSource,
            SameSourceReason = "Compare the struct conventions of compiler-generated and compiler-expanded calls with desktop .NET.",
            Optimization = optimization,
        });

    private const string Source = """
        using System;
        using System.Collections.Generic;
        using System.IO;
        using System.Runtime.CompilerServices;
        using System.Threading.Tasks;
        using System.Threading.Tasks.Sources;

        namespace NetWasm.Correctness.StructConventions;

        public struct Pair { public string Name; public object Box; public int N; }
        public struct Small { public int A; public object O; public short T; public sbyte B; }
        public struct Wide { public long A; public double B; public byte C; }
        public struct Five { public int A, B, C, D, E; }
        public struct Nested { public Pair P; public int X; }

        public interface IMeasure { int Measure(Pair p, Wide w); }

        public class Measurer : IMeasure
        {
            public virtual int Measure(Pair p, Wide w) => p.Name.Length + p.N + (int)w.A + w.C;
        }

        public sealed class Doubled : Measurer
        {
            public override int Measure(Pair p, Wide w) => base.Measure(p, w) * 2;
        }

        public sealed class Holder
        {
            public readonly int Sum;
            public Holder(Pair p, Wide w) { Sum = p.N + (int)w.B; }
        }

        public struct Built
        {
            public Pair First;
            public int Second;
            public Built(Pair first, int second) { First = first; Second = second; }
        }

        public abstract class Never
        {
            public abstract int Get(Pair p);
        }

        // Completes at once, checking its token as a value task's source must; the token starts near short's top.
        public sealed class CountingSource : IValueTaskSource<int>
        {
            private short _token = 32760;
            private int _value;

            public short Next(int value) { _value = value; return ++_token; }
            public ValueTaskSourceStatus GetStatus(short token) =>
                token == _token ? ValueTaskSourceStatus.Succeeded : throw new InvalidOperationException("token " + token);
            public int GetResult(short token) => token == _token ? _value : throw new InvalidOperationException("token " + token);
            public void OnCompleted(Action<object?> continuation, object? state, short token, ValueTaskSourceOnCompletedFlags flags) =>
                continuation(state);
        }

        public static class EntryPoint
        {
            private static int _trace = 17;
            private static readonly List<object> Kept = new List<object>();
            private static readonly object Gate = new object();
            private static readonly CountingSource Counting = new CountingSource();
            private static readonly Never? NeverMade = Nothing();

            private static void Mark(int value) => _trace = unchecked(_trace * 31 + value);

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static Never? Nothing() => null;

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static Pair Make(int i) => new Pair { Name = "n" + i, Box = new int[] { i, i + 1 }, N = i };

            // Each read follows allocations, so the reference fields stay rooted from the parameters.
            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int Use(Pair p, Wide w, Five f, Nested n, KeyValuePair<string, object> kv)
            {
                Kept.Add(new byte[48]);
                var name = p.Name.Length;
                Kept.Add(new byte[48]);
                return name + ((int[])p.Box)[1] - p.N + (int)w.A + (int)w.B + w.C + f.A + f.E + n.P.N + ((int[])n.P.Box)[0] +
                    n.X + kv.Key.Length + ((string)kv.Value).Length;
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static Pair Echo(Pair p) => p;

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static Pair Forward(Pair p) => Echo(p);

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int Mutate(Wide w)
            {
                w.A += 5;
                w.C++;
                return (int)w.A + w.C;
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int ByRef(ref Wide w)
            {
                w.A += 7;
                return (int)w.A;
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int Recurse(Pair p, int depth) =>
                depth == 0 ? p.N + p.Name.Length : Recurse(new Pair { Name = p.Name + "x", Box = p.Box, N = p.N + 1 }, depth - 1);

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static T Identity<T>(T value) => value;

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static Small Lanes(int k)
            {
                lock (Gate)
                {
                    if (k < 0) return default;
                    Small result;
                    result.A = k;
                    result.O = Gate;
                    result.T = (short)k;
                    result.B = (sbyte)k;
                    return result;
                }
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static bool SmallMatches(Small small, int k) => small.T == (short)k && small.B == (sbyte)k && small.A == k;

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static ValueTask<int> Pending(int value)
            {
                lock (Gate)
                {
                    if (value < 0) return new ValueTask<int>(Task.FromResult(-1));
                    var token = Counting.Next(value);
                    return new ValueTask<int>(Counting, token);
                }
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int Lookup(ReadOnlySpan<int> values, ReadOnlySpan<int> table, int count)
            {
                var sum = 0;
                for (var index = 0; index < count; index++) sum ^= table[values[index] & 7] + index;
                return sum;
            }

            private static async Task<int> Awaiting(int input)
            {
                var total = 0;
                for (var i = 0; i < 12; i++) total += await Pending(i + input);
                var stream = new MemoryStream(new byte[64], writable: true);
                var buffer = new byte[3];
                for (var i = 0; i < 6; i++)
                {
                    await stream.WriteAsync(new byte[] { (byte)(i + input), 2, 3 }.AsMemory(), default);
                }
                stream.Position = 0;
                for (var i = 0; i < 6; i++) total += await stream.ReadAsync(buffer.AsMemory(), default) + buffer[0];
                return total;
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int Filtered(Pair p, int input)
            {
                try
                {
                    if (input >= 0) throw new InvalidOperationException("x" + input);
                    return 0;
                }
                catch (InvalidOperationException e) when (Make(input + 3) is var made && made.Name.Length + made.N + p.N +
                    e.Message.Length > 2)
                {
                    return 7 + p.N + p.Name.Length;
                }
            }

            public static int Trace() => _trace;

            public static int Run(int input)
            {
                _trace = 17;
                Kept.Clear();
                IMeasure[] measures = { new Measurer(), new Doubled() };
                Func<Pair, Wide, int> measure = (p, w) => p.N * 3 + (int)w.B;
                for (var i = 0; i < 4 + input; i++)
                {
                    var p = Make(i + input);
                    var w = new Wide { A = i, B = i * 1.5, C = (byte)(i + 200) };
                    var f = new Five { A = i, B = 2, C = 3, D = 4, E = i * 2 };
                    var n = new Nested { P = Make(i + 1), X = input };
                    Mark(Use(p, w, f, n, new KeyValuePair<string, object>("k" + i, "v" + input)));
                    Mark(Forward(p).N + Echo(p).Name.Length);
                    Mark(Mutate(w) + (int)w.A + w.C);
                    Mark(ByRef(ref w) + (int)w.A);
                    Mark(Recurse(p, input % 3));
                    Mark(Identity(p).N + Identity(w).C);
                    Mark(measures[i & 1].Measure(p, w) + measure(p, w));
                    Mark(new Holder(p, w).Sum + new Built(p, i).First.N);
                    var k = (input + 1) * 20000 + i * 3001;
                    var small = Lanes(k);
                    Mark(SmallMatches(small, k) ? 1 : 0);
                    Mark(small.T + small.B);
                }
                var values = new int[] { 1, 2, 3, 4, 5, 6, 7, 8, 9 };
                var table = new int[] { 10, 20, 30, 40, 50, 60, 70, 80 };
                Mark(Lookup(values, table, values.Length - input % 3));
                var task = Awaiting(input);
                Mark(task.IsCompleted ? task.Result : -7);
                try { Mark(NeverMade!.Get(Make(input))); }
                catch (NullReferenceException) { Mark(99); }
                Mark(typeof(MemoryStream) == new MemoryStream().GetType() ? 3 : 4);
                Mark(Filtered(Make(input), input));
                return unchecked(_trace * 31 + Kept.Count);
            }
        }
        """;

    private const string CallersSource = """
        using System;
        using System.Collections.Generic;
        using System.Collections.ObjectModel;
        using System.Linq.Expressions;
        using System.Runtime.CompilerServices;
        using System.Runtime.InteropServices;

        namespace NetWasm.Correctness.StructConventionCallers;

        public struct Pair { public string Name; public object Box; public int N; }

        [StructLayout(LayoutKind.Explicit)]
        public struct Buf12
        {
            [FieldOffset(0)] public uint U0;
            [FieldOffset(4)] public uint U1;
            [FieldOffset(8)] public uint U2;
            [FieldOffset(0)] private ulong _low64;
            [FieldOffset(4)] private ulong _high64;
            public ulong Low64 { get => _low64; set => _low64 = value; }
            public ulong High64 { get => _high64; set => _high64 = value; }
        }

        public static class EntryPoint
        {
            private static int _trace = 17;

            private static void Mark(int value) => _trace = unchecked(_trace * 31 + value);

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static Pair Make(int i) => new Pair { Name = "n" + i, Box = new int[] { i, i + 1 }, N = i };

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            private static uint Div96By32(ref Buf12 bufNum, uint den)
            {
                ulong tmp, div, rem;
                if (bufNum.U2 != 0)
                {
                    tmp = bufNum.High64;
                    (div, rem) = Math.DivRem(tmp, den);
                    bufNum.High64 = div;
                    tmp = (rem << 32) | bufNum.U0;
                    if (tmp == 0) return 0;
                    (div, rem) = Math.DivRem(tmp, den);
                    bufNum.U0 = (uint)div;
                    return (uint)rem;
                }
                tmp = bufNum.Low64;
                if (tmp == 0) return 0;
                (bufNum.Low64, rem) = Math.DivRem(tmp, den);
                return (uint)rem;
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int Divide(ulong low, uint high, uint den)
            {
                Unsafe.SkipInit(out Buf12 buf);
                buf.Low64 = low;
                buf.U2 = high;
                var remainder = Div96By32(ref buf, den);
                return unchecked((int)(buf.U2 * 7 + buf.U1 * 5 + buf.U0 * 3 + remainder));
            }

            public static Pair Grow(Pair p, decimal by) => new Pair { Name = p.Name + "+", Box = p.Box, N = p.N + (int)by };

            private static int Generated(int input)
            {
                var hash = Divide(50UL + (ulong)input, 0, 3);
                hash = unchecked(hash * 31 + Divide(0x1_0000_0007UL + (ulong)input, 3, 7));
                var (quotient, remainder) = UInt128.DivRem(((UInt128)ulong.MaxValue << 3) + (uint)input, 1000);
                hash = unchecked(hash * 31 + (int)(ulong)quotient + (int)(ulong)remainder);
                // A divisor above the dividend: DivRem returns (Zero, left), built through the tuple's struct fields.
                var below = new UInt128(0, 0x8000_0000_0000_0000UL + (ulong)input) % new UInt128(1, (ulong)input);
                hash = unchecked(hash * 31 + (int)(ulong)below + (int)((ulong)below >> 40));
                var third = (decimal)(input + 5) / 3m;
                hash = unchecked(hash * 31 + (int)(third * 1000m) + decimal.ToInt32(decimal.Truncate(third * 7)));
                Expression<Func<Pair, decimal, Pair>> growTree = (p, by) => Grow(p, by);
                var grown = growTree.Compile()(Make(input), 2.5m);
                hash = unchecked(hash * 31 + grown.N + grown.Name.Length);
                Func<Pair, decimal, Pair> growing = Grow;
                var invoked = (Pair)growing.DynamicInvoke(Make(input + 1), 1.5m)!;
                hash = unchecked(hash * 31 + invoked.N + invoked.Name.Length);
                Action<Pair> each = p => Mark(p.N);
                each += p => Mark(p.Name.Length + ((int[])p.Box)[1]);
                each(Make(input + 2));
                IList<decimal> list = new decimal[] { 1m, 2m, 3m + input };
                list[1] = 9.5m;
                hash = unchecked(hash * 31 + list.IndexOf(9.5m) + (int)list[2] + (list.Contains(1m) ? 1 : 0));
                var collection = new ObservableCollection<decimal> { 1m };
                collection.Insert(0, 2.5m + input);
                hash = unchecked(hash * 31 + (int)(collection[0] * 10) + collection.Count);
                return hash;
            }

            public static int Trace() => _trace;

            public static int Run(int input)
            {
                _trace = 17;
                Mark(Generated(input));
                return _trace;
            }
        }
        """;
}
