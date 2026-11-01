namespace NetWasm.Compiler.Tests.Correctness;

public sealed class InterlockedTests(CorrectnessTestRunner runner)
{
    [Fact]
    public void PortableOperationsPreserveDesktopSequentialSemantics() =>
        runner.Run(new("InterlockedContracts", "InterlockedContracts", Source, [42])
        {
            ExpectedReturnValue = 42,
            Matrix = new(CorpusMatrixProfile.Extended, Backend: CorpusExecutionBackend.Linked),
        });

    [Fact]
    public void NullLocationsThrowBeforeReadingOrWriting() =>
        runner.Run(new("InterlockedNullLocations", "InterlockedNullLocations", NullSource, [42])
        {
            ExpectedReturnValue = 42,
            Matrix = new(CorpusMatrixProfile.Extended, Backend: CorpusExecutionBackend.Linked),
        });

    internal const string Source = """
        using System;
        using System.Threading;
        namespace InterlockedContracts;

        public sealed class EqualObject
        {
            public override bool Equals(object? other) => other is EqualObject;
            public override int GetHashCode() => 0;
        }

        public static class EntryPoint
        {
            public static int Trace() => 0;

            public static int Run(int input)
            {
                int i = int.MaxValue;
                long l = long.MaxValue;
                checked
                {
                    if (Interlocked.Increment(ref i) != int.MinValue || i != int.MinValue) return 1;
                    if (Interlocked.Decrement(ref i) != int.MaxValue || i != int.MaxValue) return 2;
                    if (Interlocked.Add(location1: ref i, value: 2) != int.MinValue + 1 || i != int.MinValue + 1) return 3;
                    if (Interlocked.Increment(ref l) != long.MinValue || l != long.MinValue) return 4;
                    if (Interlocked.Decrement(ref l) != long.MaxValue || l != long.MaxValue) return 5;
                    if (Interlocked.Add(location1: ref l, value: 2) != long.MinValue + 1 || l != long.MinValue + 1) return 6;
                }
                if (Interlocked.Exchange(ref i, input) != int.MinValue + 1 || i != input) return 7;
                if (Interlocked.CompareExchange(ref i, 1, input + 1) != input || i != input) return 8;
                if (Interlocked.CompareExchange(ref i, 7, input) != input || i != 7) return 9;
                if (Interlocked.Exchange(ref l, 1L << 40) != long.MinValue + 1 || l != 1L << 40) return 10;
                if (Interlocked.Read(ref l) != 1L << 40 || l != 1L << 40) return 11;
                if (Interlocked.CompareExchange(ref l, 7, 1L << 39) != 1L << 40 || l != 1L << 40) return 12;
                if (Interlocked.CompareExchange(ref l, -7, 1L << 40) != 1L << 40 || l != -7) return 13;

                nint pointer = 123;
                if (Interlocked.Exchange(ref pointer, 456) != 123 || pointer != 456) return 14;
                if (Interlocked.CompareExchange(ref pointer, 789, 123) != 456 || pointer != 456) return 15;
                if (Interlocked.CompareExchange(ref pointer, -1, 456) != 456 || pointer != -1) return 16;
                if (IntPtr.Size == 8)
                {
                    var high = unchecked((nint)(1L << 40));
                    if (Interlocked.Exchange(ref pointer, high) != -1 || pointer != high) return 17;
                    if (Interlocked.CompareExchange(ref pointer, 0, high) != high || pointer != 0) return 18;
                }

                var first = new EqualObject();
                var equalButDistinct = new EqualObject();
                object? reference = first;
                if (!ReferenceEquals(Interlocked.CompareExchange(ref reference, null, equalButDistinct), first)
                    || !ReferenceEquals(reference, first)) return 19;
                if (!ReferenceEquals(Interlocked.CompareExchange(ref reference, equalButDistinct, first), first)
                    || !ReferenceEquals(reference, equalButDistinct)) return 20;
                if (!ReferenceEquals(Interlocked.Exchange(ref reference, null), equalButDistinct)
                    || reference != null) return 21;
                if (Interlocked.CompareExchange(ref reference, first, null) != null
                    || !ReferenceEquals(reference, first)) return 22;
                EqualObject? generic = first;
                if (!ReferenceEquals(Interlocked.CompareExchange(ref generic, null, equalButDistinct), first)
                    || !ReferenceEquals(generic, first)) return 23;
                if (!ReferenceEquals(Interlocked.CompareExchange(ref generic, equalButDistinct, first), first)
                    || !ReferenceEquals(generic, equalButDistinct)) return 24;
                if (!ReferenceEquals(Interlocked.Exchange(ref generic, null), equalButDistinct)
                    || generic != null) return 25;
                if (Interlocked.CompareExchange(ref generic, first, null) != null
                    || !ReferenceEquals(generic, first)) return 26;

                var floating = FloatingPoint();
                if (floating != 0) return floating;
                var bitwise = Bitwise();
                if (bitwise != 0) return bitwise;
                Interlocked.MemoryBarrier();
                Interlocked.MemoryBarrierProcessWide();
                return input;
            }

            private static int FloatingPoint()
            {
                float f = BitConverter.Int32BitsToSingle(unchecked((int)0x80000000));
                if (BitConverter.SingleToInt32Bits(Interlocked.CompareExchange(ref f, 3, 0)) != unchecked((int)0x80000000)
                    || BitConverter.SingleToInt32Bits(f) != unchecked((int)0x80000000)) return 30;
                if (BitConverter.SingleToInt32Bits(Interlocked.CompareExchange(ref f, 3,
                    BitConverter.Int32BitsToSingle(unchecked((int)0x80000000)))) != unchecked((int)0x80000000)
                    || f != 3) return 31;
                var nan = BitConverter.Int32BitsToSingle(0x7fc00001);
                if (Interlocked.Exchange(ref f, nan) != 3 || BitConverter.SingleToInt32Bits(f) != 0x7fc00001) return 32;
                if (BitConverter.SingleToInt32Bits(Interlocked.CompareExchange(ref f, 4,
                    BitConverter.Int32BitsToSingle(0x7fc00002))) != 0x7fc00001
                    || BitConverter.SingleToInt32Bits(f) != 0x7fc00001) return 33;
                if (BitConverter.SingleToInt32Bits(Interlocked.CompareExchange(ref f, 4, nan)) != 0x7fc00001
                    || f != 4) return 34;

                double d = BitConverter.Int64BitsToDouble(unchecked((long)0x8000000000000000));
                if (BitConverter.DoubleToInt64Bits(Interlocked.CompareExchange(ref d, 3, 0)) != unchecked((long)0x8000000000000000)
                    || BitConverter.DoubleToInt64Bits(d) != unchecked((long)0x8000000000000000)) return 35;
                if (BitConverter.DoubleToInt64Bits(Interlocked.CompareExchange(ref d, 3,
                    BitConverter.Int64BitsToDouble(unchecked((long)0x8000000000000000)))) != unchecked((long)0x8000000000000000)
                    || d != 3) return 36;
                var doubleNan = BitConverter.Int64BitsToDouble(0x7ff8000000000001);
                if (Interlocked.Exchange(ref d, doubleNan) != 3 || BitConverter.DoubleToInt64Bits(d) != 0x7ff8000000000001) return 37;
                if (BitConverter.DoubleToInt64Bits(Interlocked.CompareExchange(ref d, 4,
                    BitConverter.Int64BitsToDouble(0x7ff8000000000002))) != 0x7ff8000000000001
                    || BitConverter.DoubleToInt64Bits(d) != 0x7ff8000000000001) return 38;
                if (BitConverter.DoubleToInt64Bits(Interlocked.CompareExchange(ref d, 4, doubleNan)) != 0x7ff8000000000001
                    || d != 4) return 39;
                return 0;
            }

            private static int Bitwise()
            {
                int i = unchecked((int)0x80000003);
                if (Interlocked.And(ref i, 3) != unchecked((int)0x80000003) || i != 3) return 50;
                if (Interlocked.Or(ref i, int.MinValue) != 3 || i != unchecked((int)0x80000003)) return 51;
                uint u = 0x80000003;
                if (Interlocked.And(ref u, 3) != 0x80000003 || u != 3) return 52;
                if (Interlocked.Or(ref u, 0x80000000) != 3 || u != 0x80000003) return 53;
                long l = unchecked((long)0x8000000000000003);
                if (Interlocked.And(ref l, 3) != unchecked((long)0x8000000000000003) || l != 3) return 54;
                if (Interlocked.Or(ref l, long.MinValue) != 3 || l != unchecked((long)0x8000000000000003)) return 55;
                ulong ul = 0x8000000000000003;
                if (Interlocked.And(ref ul, 3) != 0x8000000000000003 || ul != 3) return 56;
                if (Interlocked.Or(ref ul, 0x8000000000000000) != 3 || ul != 0x8000000000000003) return 57;
                return 0;
            }
        }
        """;
    private const string NullSource = """
        using System;
        using System.Threading;
        using System.Runtime.CompilerServices;
        namespace InterlockedNullLocations;
        public static class EntryPoint
        {
            public static int Trace() => 0;
            public static int Run(int input)
            {
                for (var operation = 0; operation < 29; operation++)
                {
                    try
                    {
                        switch (operation)
                        {
                            case 0: Interlocked.Read(ref Unsafe.NullRef<long>()); break;
                            case 1: Interlocked.Increment(ref Unsafe.NullRef<int>()); break;
                            case 2: Interlocked.Increment(ref Unsafe.NullRef<long>()); break;
                            case 3: Interlocked.Decrement(ref Unsafe.NullRef<int>()); break;
                            case 4: Interlocked.Decrement(ref Unsafe.NullRef<long>()); break;
                            case 5: Interlocked.Add(ref Unsafe.NullRef<int>(), 1); break;
                            case 6: Interlocked.Add(ref Unsafe.NullRef<long>(), 1); break;
                            case 7: Interlocked.Exchange(ref Unsafe.NullRef<int>(), 1); break;
                            case 8: Interlocked.Exchange(ref Unsafe.NullRef<long>(), 1); break;
                            case 9: Interlocked.Exchange(ref Unsafe.NullRef<nint>(), 1); break;
                            case 10: Interlocked.Exchange(ref Unsafe.NullRef<float>(), 1); break;
                            case 11: Interlocked.Exchange(ref Unsafe.NullRef<double>(), 1); break;
                            case 12: Interlocked.Exchange(ref Unsafe.NullRef<object?>(), null); break;
                            case 13: Interlocked.Exchange<string?>(ref Unsafe.NullRef<string?>(), null); break;
                            case 14: Interlocked.CompareExchange(ref Unsafe.NullRef<int>(), 1, 1); break;
                            case 15: Interlocked.CompareExchange(ref Unsafe.NullRef<long>(), 1, 1); break;
                            case 16: Interlocked.CompareExchange(ref Unsafe.NullRef<nint>(), 1, 1); break;
                            case 17: Interlocked.CompareExchange(ref Unsafe.NullRef<float>(), 1, 1); break;
                            case 18: Interlocked.CompareExchange(ref Unsafe.NullRef<double>(), 1, 1); break;
                            case 19: Interlocked.CompareExchange(ref Unsafe.NullRef<object?>(), null, null); break;
                            case 20: Interlocked.CompareExchange<string?>(ref Unsafe.NullRef<string?>(), null, null); break;
                            case 21: Interlocked.And(ref Unsafe.NullRef<int>(), 1); break;
                            case 22: Interlocked.And(ref Unsafe.NullRef<uint>(), 1); break;
                            case 23: Interlocked.And(ref Unsafe.NullRef<long>(), 1); break;
                            case 24: Interlocked.And(ref Unsafe.NullRef<ulong>(), 1); break;
                            case 25: Interlocked.Or(ref Unsafe.NullRef<int>(), 1); break;
                            case 26: Interlocked.Or(ref Unsafe.NullRef<uint>(), 1); break;
                            case 27: Interlocked.Or(ref Unsafe.NullRef<long>(), 1); break;
                            case 28: Interlocked.Or(ref Unsafe.NullRef<ulong>(), 1); break;
                        }
                        return operation + 100;
                    }
                    catch (NullReferenceException) { }
                }
                return input;
            }
        }
        """;
}
