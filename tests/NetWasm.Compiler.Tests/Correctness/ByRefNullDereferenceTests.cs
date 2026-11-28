// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

namespace NetWasm.Compiler.Tests.Correctness;

// Reading or writing through a null managed address throws NullReferenceException, as on desktop .NET, where the
// access faults. Wasm memory has an address 0, so before the compiler checked, such a read returned whatever was
// there and a write overwrote it. Every instruction that dereferences an address: a scalar and a struct read and
// write, a struct cleared, a field read, written and addressed through the reference, a ref parameter, a struct
// method's this, Unsafe's offsetting and a span over the reference; and addresses that cannot be null still work.
public sealed class ByRefNullDereferenceTests(CorrectnessTestRunner runner) : CSharpSemanticTestBase(runner)
{
    [Fact]
    public void ReadsAndWritesThroughANullManagedAddressThrowNullReferenceException() => Run(new(
        "ByRefNullDereference", "NetWasm.Correctness.ByRefNullDereference",
        """
        using System;
        using System.Runtime.CompilerServices;
        using System.Runtime.InteropServices;
        using System.Threading;
        namespace NetWasm.Correctness.ByRefNullDereference;
        public struct Pair
        {
            public int A;
            public int B;
            public int Sum() => A + B;
        }
        public static class EntryPoint
        {
            private static int s_static = 5;
            private static Pair s_pair = new() { A = 1, B = 2 };

            public static int RunDesktop(int input) => Run(input);
            public static int Trace() => 0;

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int ReadParameter(ref int value) => value + 1000;

            private static int ReadInlined(ref int value) => value + 2000;

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static void WriteParameter(ref Pair pair, int value) => pair.B = value;

            public static int Run(int input)
            {
                try
                {
                    ref var number = ref Unsafe.NullRef<int>();
                    ref var pair = ref Unsafe.NullRef<Pair>();
                    switch (input)
                    {
                        case 0: return number + 1000;
                        case 1: number = 5; return 2;
                        case 2: { var copy = pair; return copy.A + copy.B + 1000; }
                        case 3: pair = new Pair { A = 1, B = 2 }; return 2;
                        case 4: pair = default; return 2;
                        case 5: return pair.A + 1000;
                        case 6: pair.B = 7; return 2;
                        case 7: { ref var field = ref pair.B; return field + 1000; }
                        case 8: return ReadParameter(ref number);
                        case 9: return ReadInlined(ref number);
                        case 10: WriteParameter(ref pair, 3); return 2;
                        case 11: return pair.Sum() + 1000;
                        case 12: return Unsafe.Add(ref number, 0) + 1000;
                        case 13: return MemoryMarshal.CreateSpan(ref number, 1)[0] + 1000;
                        case 14: return Interlocked.Increment(ref number);
                        case 15: Volatile.Write(ref number, 1); return 2;
                        case 16:
                        {
                            // The first access throws; the loop never finishes.
                            var sum = 0;
                            for (var i = 0; i < 4; i++) sum += Unsafe.Add(ref number, 0);
                            return sum + 1000;
                        }
                        // Addresses that cannot be null: a local, an array element, a static, a field of an object and
                        // of a struct in an array, each read and written.
                        case 17:
                        {
                            var local = 41;
                            ref var value = ref local;
                            value++;
                            var array = new[] { 1, 2, 3 };
                            ref var element = ref array[input % 3];
                            element += 10;
                            ref var shared = ref s_static;
                            shared *= 2;
                            var pairs = new Pair[2];
                            ref var held = ref pairs[1];
                            held.A = 4;
                            held.B = held.A + ReadParameter(ref value) + ReadInlined(ref element);
                            ref var staticPair = ref s_pair;
                            staticPair.B += held.Sum();
                            return value + element + shared + held.B + staticPair.Sum() + array[2];
                        }
                        default:
                        {
                            var box = new StrongBox<Pair>(new Pair { A = 6, B = 7 });
                            ref var boxed = ref box.Value;
                            boxed.A += boxed.Sum();
                            Span<int> span = stackalloc int[3];
                            ref var first = ref MemoryMarshal.GetReference(span);
                            Unsafe.Add(ref first, 2) = input;
                            return boxed.A * 100 + span[2] + Unsafe.Add(ref first, 2);
                        }
                    }
                }
                catch (NullReferenceException) { return -1; }
                catch (Exception) { return -2; }
            }
        }
        """, [.. Enumerable.Range(0, 19)]));
}
