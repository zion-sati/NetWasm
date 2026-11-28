// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

using static CompilerTestSupport;

// Volatile's and Interlocked's calls are lowered to the loads and stores they are on one thread: every overload of
// Volatile.Read and Write, Interlocked.Read, the barriers, Exchange (generic over an enum and a reference too),
// CompareExchange (every width, floats by their bits: a NaN matches its NaN, +0 and -0 differ), And and Or (generic
// over enums too), Add, Increment and Decrement wrapping, and a null location throwing from each, as desktop .NET 11
// computes them. No Volatile or Interlocked method is left to call.
public sealed class InterlockedLoweringCompilationTests
{
    private const string Source = """
        using System;
        using System.Runtime.CompilerServices;
        using System.Threading;

        public enum Small : byte { A = 1, B = 2, C = 200 }
        public enum Wide : long { A = 1, B = 1L << 40 }
        public sealed class Box { public int Value; public Box(int value) => Value = value; }

        public static class Operations
        {
            private static long s_hash;

            private static void Mix(long value) => s_hash = unchecked(s_hash * 31 + value);

            private static void Mix(double value) => Mix(BitConverter.DoubleToInt64Bits(value));

            private static int Thrown(Func<long> run)
            {
                try { Mix(run()); return 0; }
                catch (NullReferenceException) { return 1; }
            }

            public static int Run(int input)
            {
                s_hash = 17;
                // Volatile, every overload.
                bool flag = input > 3; Volatile.Write(ref flag, !Volatile.Read(ref flag)); Mix(flag ? 1 : 0);
                byte b = (byte)(input * 37); Volatile.Write(ref b, (byte)(Volatile.Read(ref b) + 250)); Mix(b);
                sbyte sb = (sbyte)(input - 100); Volatile.Write(ref sb, (sbyte)(Volatile.Read(ref sb) - 50)); Mix(sb);
                short s = (short)(input * -1000); Volatile.Write(ref s, (short)(Volatile.Read(ref s) * 3)); Mix(s);
                ushort us = (ushort)(input * 3000); Volatile.Write(ref us, (ushort)(Volatile.Read(ref us) + 60000)); Mix(us);
                int i = input; Volatile.Write(ref i, Volatile.Read(ref i) * -7); Mix(i);
                uint ui = (uint)input; Volatile.Write(ref ui, Volatile.Read(ref ui) - 9); Mix(ui);
                long l = input * 0x1_0000_0001L; Volatile.Write(ref l, Volatile.Read(ref l) ^ -5); Mix(l);
                ulong ul = (ulong)input << 40; Volatile.Write(ref ul, Volatile.Read(ref ul) + 3); Mix((long)ul);
                nint n = input - 3; Volatile.Write(ref n, Volatile.Read(ref n) * 5); Mix(n);
                nuint nu = (nuint)(uint)(input & 0xff) + 2u; Volatile.Write(ref nu, Volatile.Read(ref nu) * 4); Mix((long)nu);
                float f = input * 0.5f; Volatile.Write(ref f, Volatile.Read(ref f) - 1.25f); Mix(f);
                double d = input * 0.25; Volatile.Write(ref d, Volatile.Read(ref d) * -3); Mix(d);
                Box? box = new(input); Volatile.Write(ref box, new Box(Volatile.Read(ref box)!.Value + 1)); Mix(box!.Value);
                // Interlocked.Read and the barriers.
                Mix(Interlocked.Read(ref l)); Mix((long)Interlocked.Read(ref ul));
                Interlocked.MemoryBarrier(); Interlocked.MemoryBarrierProcessWide();
                // Exchange: the old value, the new one stored.
                Mix(Interlocked.Exchange(ref b, (byte)(input + 1))); Mix(b);
                Mix(Interlocked.Exchange(ref sb, (sbyte)(-input))); Mix(sb);
                Mix(Interlocked.Exchange(ref s, (short)(input - 30000))); Mix(s);
                Mix(Interlocked.Exchange(ref us, (ushort)(input + 65000))); Mix(us);
                Mix(Interlocked.Exchange(ref i, input * 11)); Mix(i);
                Mix(Interlocked.Exchange(ref ui, (uint)input + 4000000000u)); Mix(ui);
                Mix(Interlocked.Exchange(ref l, -input)); Mix(l);
                Mix((long)Interlocked.Exchange(ref ul, ulong.MaxValue - (ulong)input)); Mix((long)ul);
                Mix(Interlocked.Exchange(ref n, -2 * input)); Mix(n);
                Mix((long)Interlocked.Exchange(ref nu, (nuint)(uint)(input & 0xffff))); Mix((long)nu);
                Mix(Interlocked.Exchange(ref f, -input)); Mix(f);
                Mix(Interlocked.Exchange(ref d, double.NaN)); Mix(d);
                Mix(Interlocked.Exchange(ref box, new Box(-input))!.Value); Mix(box!.Value);
                var small = (Small)(input & 3); Mix((long)Interlocked.Exchange(ref small, Small.C)); Mix((long)small);
                object? item = "x"; Mix(Interlocked.Exchange(ref item, input) is string ? 1 : 2);
                // CompareExchange: a match stores, a mismatch does not; floats compare their bits.
                b = 7; Mix(Interlocked.CompareExchange(ref b, 9, 7)); Mix(Interlocked.CompareExchange(ref b, 1, 7)); Mix(b);
                sb = -3; Mix(Interlocked.CompareExchange(ref sb, 5, -3)); Mix(Interlocked.CompareExchange(ref sb, 1, -3)); Mix(sb);
                s = -300; Mix(Interlocked.CompareExchange(ref s, 12, -300)); Mix(s);
                us = 65535; Mix(Interlocked.CompareExchange(ref us, 4, 65535)); Mix(us);
                i = input; Mix(Interlocked.CompareExchange(ref i, 100, input)); Mix(Interlocked.CompareExchange(ref i, 5, input)); Mix(i);
                ui = 4000000000u; Mix(Interlocked.CompareExchange(ref ui, 1u, 4000000000u)); Mix(ui);
                l = long.MinValue + input; Mix(Interlocked.CompareExchange(ref l, 8, long.MinValue + input)); Mix(l);
                ul = ulong.MaxValue; Mix((long)Interlocked.CompareExchange(ref ul, 2ul, ulong.MaxValue)); Mix((long)ul);
                n = -input; Mix(Interlocked.CompareExchange(ref n, 6, -input)); Mix(n);
                nu = 3; Mix((long)Interlocked.CompareExchange(ref nu, (nuint)(uint)(input & 0xff), 4)); Mix((long)nu);
                f = float.NaN; Mix(Interlocked.CompareExchange(ref f, 1f, float.NaN)); Mix(f);
                f = 0f; Mix(Interlocked.CompareExchange(ref f, 2f, -0f)); Mix(f);
                d = -0.0; Mix(Interlocked.CompareExchange(ref d, 3.5, 0.0)); Mix(Interlocked.CompareExchange(ref d, 4.5, -0.0)); Mix(d);
                d = double.NaN; Mix(Interlocked.CompareExchange(ref d, 7.0, double.NaN)); Mix(d);
                var first = box; Mix(Interlocked.CompareExchange(ref box, new Box(55), first)!.Value); Mix(box!.Value);
                Mix(Interlocked.CompareExchange(ref box, new Box(66), first)!.Value); Mix(box!.Value);
                // And, Or: the old value, the combination stored.
                i = input | 0x70; Mix(Interlocked.And(ref i, 0x5a)); Mix(i); Mix(Interlocked.Or(ref i, 0x101)); Mix(i);
                ui = 0xF0F0F0F0u; Mix(Interlocked.And(ref ui, 0x0FF00FF0u)); Mix(ui); Mix(Interlocked.Or(ref ui, (uint)input)); Mix(ui);
                l = -1; Mix(Interlocked.And(ref l, 0x0F0F_0000_0000_FFFFL)); Mix(l); Mix(Interlocked.Or(ref l, (long)input << 33)); Mix(l);
                ul = 0x8000_0000_0000_0001; Mix((long)Interlocked.And(ref ul, ulong.MaxValue >> 1)); Mix((long)ul); Mix((long)Interlocked.Or(ref ul, 6)); Mix((long)ul);
                small = Small.C; Mix((long)Interlocked.And(ref small, Small.B | Small.A)); Mix((long)small);
                var wide = Wide.A; Mix((long)Interlocked.Or(ref wide, Wide.B)); Mix((long)wide);
                // Add, Increment, Decrement: the new value, wrapping.
                i = int.MaxValue - input; Mix(Interlocked.Add(ref i, input + 1)); Mix(Interlocked.Increment(ref i)); Mix(Interlocked.Decrement(ref i)); Mix(i);
                ui = 0; Mix(Interlocked.Decrement(ref ui)); Mix(Interlocked.Add(ref ui, (uint)input)); Mix(Interlocked.Increment(ref ui)); Mix(ui);
                l = long.MaxValue; Mix(Interlocked.Increment(ref l)); Mix(Interlocked.Add(ref l, -input)); Mix(Interlocked.Decrement(ref l)); Mix(l);
                ul = 1; Mix((long)Interlocked.Add(ref ul, ulong.MaxValue)); Mix((long)Interlocked.Decrement(ref ul)); Mix((long)Interlocked.Increment(ref ul)); Mix((long)ul);
                // A null location throws as desktop .NET does, from each kind of call.
                var thrown =
                    Thrown(() => Volatile.Read(ref Unsafe.NullRef<int>())) +
                    Thrown(() => { Volatile.Write(ref Unsafe.NullRef<long>(), 1); return 0; }) * 2 +
                    Thrown(() => Interlocked.Exchange(ref Unsafe.NullRef<int>(), 1)) * 4 +
                    Thrown(() => Interlocked.CompareExchange(ref Unsafe.NullRef<short>(), 1, 0)) * 8 +
                    Thrown(() => Interlocked.Add(ref Unsafe.NullRef<long>(), 1)) * 16 +
                    Thrown(() => Interlocked.And(ref Unsafe.NullRef<uint>(), 1)) * 32 +
                    Thrown(() => Interlocked.Increment(ref Unsafe.NullRef<int>())) * 64 +
                    Thrown(() => Interlocked.Read(ref Unsafe.NullRef<long>())) * 128 +
                    Thrown(() => (long)Interlocked.CompareExchange(ref Unsafe.NullRef<double>(), 1, 0)) * 256;
                Mix(thrown);
                return (int)(s_hash ^ (s_hash >> 32));
            }
        }

        public static class EntryPoint
        {
            public static int Run(int input) => Operations.Run(input);
        }
        """;

    // As desktop .NET 11 computes them (Debug, and Release with tiered compilation off, agree). Native integers stay
    // within 32 bits, so wasm32 computes what 64-bit desktop does.
    private static readonly (int Input, int Expected)[] Cases =
        [(0, 876217880), (1, 44174269), (7, 368865801), (-5, 1398459926), (42, -2066245316)];

    [Theory]
    [InlineData(FinalWasmOptimization.None, WasmTarget.Wasm32)]
    [InlineData(FinalWasmOptimization.O3, WasmTarget.Wasm32)]
    [InlineData(FinalWasmOptimization.O3, WasmTarget.Wasm64)]
    public void EveryOverloadComputesWhatDesktopComputes(FinalWasmOptimization optimization, WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileOptimizedSource("InterlockedLowering", Source);
        var result = NetWasmCompiler.Compile(new CompilerOptions(
            assembly, [assets.CoreLib], "EntryPoint", "Run", [], Target: target, Optimization: optimization));

        Assert.Empty(result.Program.Methods.Values.Concat(result.Program.ConstructedMethods.Values)
            .Where(static body => body.Method.DeclaringType.FullName is "System.Threading.Volatile" or "System.Threading.Interlocked")
            .Select(static body => body.Method.Definition.Name + "(" + string.Join(",",
                body.Method.Signature.ParameterSignatureTypes.Select(static parameter => parameter.CanonicalName)) + ")"));
        foreach (var (input, expected) in Cases)
        {
            Assert.Equal(expected, ExecuteWithStandardWasiNode(result.ApplicationModule, assets.Directory, input, target, result.StaticDataEnd));
        }
    }
}
