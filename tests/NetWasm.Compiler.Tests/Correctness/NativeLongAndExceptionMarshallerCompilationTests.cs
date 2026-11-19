// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

namespace NetWasm.Compiler.Tests.Correctness;

// CLong and CULong, C's long as upstream's non-Windows build defines it (pointer-sized): construction, equality,
// hashing, formatting, boxing and generic use. The exception marshallers the COM source generator uses convert an
// exception to its HResult, NaN, a default value or nothing. Each is compared with desktop .NET.
public sealed class NativeLongAndExceptionMarshallerCompilationTests(CorrectnessTestRunner runner)
{
    [Fact]
    public void NativeLongsAndExceptionMarshallersMatchDesktop() =>
        runner.Run(new("NativeLongAndExceptionMarshaller", "NetWasm.Correctness.NativeLong", Source, [0, 1, 2, 3])
        {
            AllowUnsafe = true,
            ExecuteWasm64 = true,
            ExecuteOptimizedWasm = true,
            ReportAllMismatches = true,
        });

    private const string Source = """
        using System;
        using System.Runtime.CompilerServices;
        using System.Runtime.InteropServices;
        using System.Runtime.InteropServices.Marshalling;

        namespace NetWasm.Correctness.NativeLong;

        public static unsafe class EntryPoint
        {
            private static int _trace;

            public static int Trace() => _trace;

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static T Through<T>(T value) => value;

            private static int Mix(int hash, long value) => unchecked(hash * 31 + (int)value + (int)(value >> 32));

            public static int Run(int input)
            {
                var hash = 17;
                switch (input)
                {
                    case 0:
                        var a = new CLong(input - 123_456);
                        var b = new CLong((nint)987_654);
                        hash = Mix(hash, a.Value);
                        hash = Mix(hash, b.Value);
                        hash = Mix(hash, a.Equals(new CLong(-123_456)) ? 1 : 2);
                        hash = Mix(hash, a.Equals((object)b) ? 3 : 4);
                        hash = Mix(hash, a.Equals((object)a.Value) ? 5 : 6);
                        hash = Mix(hash, a.GetHashCode() == new CLong(-123_456).GetHashCode() ? 7 : 8);
                        hash = Mix(hash, a.ToString().Length);
                        hash = Mix(hash, b.ToString() == "987654" ? 9 : 10);
                        break;
                    case 1:
                        var u = new CULong(uint.MaxValue);
                        var v = new CULong((nuint)42);
                        hash = Mix(hash, (long)u.Value);
                        hash = Mix(hash, (long)v.Value);
                        hash = Mix(hash, u.Equals(new CULong(uint.MaxValue)) ? 1 : 2);
                        hash = Mix(hash, v.Equals((object)u) ? 3 : 4);
                        hash = Mix(hash, u.ToString() == "4294967295" ? 5 : 6);
                        hash = Mix(hash, u.GetHashCode() == new CULong(uint.MaxValue).GetHashCode() ? 7 : 8);
                        break;
                    case 2:
                        hash = Mix(hash, Through(new CLong(-21)).Value * 2);
                        hash = Mix(hash, (long)Through(new CULong(40u)).Value + 2);
                        object boxed = new CLong(int.MaxValue / 3);
                        hash = Mix(hash, boxed is CLong unboxed ? unboxed.Value * 3 : -1);
                        hash = Mix(hash, boxed.Equals(new CLong(int.MaxValue / 3)) ? 1 : 2);
                        Span<CLong> values = [new(1), new(-2), new(3)];
                        long sum = 0;
                        foreach (var value in values) sum += value.Value;
                        hash = Mix(hash, sum);
                        break;
                    default:
                        var failure = new InvalidOperationException("marshal");
                        hash = Mix(hash, ExceptionAsHResultMarshaller<int>.ConvertToUnmanaged(failure));
                        hash = Mix(hash, ExceptionAsHResultMarshaller<uint>.ConvertToUnmanaged(new ArgumentException()));
                        hash = Mix(hash, double.IsNaN(ExceptionAsNaNMarshaller<double>.ConvertToUnmanaged(failure)) ? 1 : 2);
                        hash = Mix(hash, float.IsNaN(ExceptionAsNaNMarshaller<float>.ConvertToUnmanaged(failure)) ? 3 : 4);
                        hash = Mix(hash, ExceptionAsDefaultMarshaller<long>.ConvertToUnmanaged(failure));
                        ExceptionAsVoidMarshaller.ConvertToUnmanaged(failure);
                        hash = Mix(hash, 5);
                        break;
                }
                _trace = hash;
                return hash;
            }
        }
        """;
}
