// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

public sealed class HashCodeCompilationTests
{
    [Theory]
    [InlineData(false, WasmTarget.Wasm32)]
    [InlineData(false, WasmTarget.Wasm64)]
    [InlineData(true, WasmTarget.Wasm32)]
    [InlineData(true, WasmTarget.Wasm64)]
    public void CompilerUsesEntropyForUnseededRandomAndHashCode(
        bool optimize,
        WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var source = """
            using System;

            namespace RandomEntropyFixture;

            public static class EntryPoint
            {
                public static int Run(int input)
                {
                    var first = new Random();
                    var second = new Random();
                    var identical = true;
                    for (var index = 0; index < 8; index++)
                    {
                        identical &= first.NextInt64() == second.NextInt64();
                    }

                    if (identical) return 1;

                    var seededFirst = new Random(42);
                    var seededSecond = new Random(42);
                    for (var index = 0; index < 16; index++)
                    {
                        if (seededFirst.Next() != seededSecond.Next()) return 2;
                    }

                    var combined = HashCode.Combine(1, 2, 3, 4);
                    if (combined != HashCode.Combine(1, 2, 3, 4)) return 3;

                    return input;
                }
            }
            """;
        ICompilationScenarioExecutor executor = new CompilationScenarioExecutor(assets);
        var result = executor.Execute(new CompilationScenario(
            $"RandomEntropyFixture_{optimize}_{target}",
            source,
            "RandomEntropyFixture.EntryPoint",
            optimize,
            target,
            42,
            []));

        Assert.Equal(42, result);
    }

    [Theory]
    [InlineData(false, WasmTarget.Wasm32)]
    [InlineData(false, WasmTarget.Wasm64)]
    [InlineData(true, WasmTarget.Wasm32)]
    [InlineData(true, WasmTarget.Wasm64)]
    public void CompilerExecutesHashCodeContracts(bool optimize, WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var source = """
            #pragma warning disable CS0618
            using System;
            using System.Collections.Generic;

            namespace HashCodeFixture;

            public sealed class DoubledHashComparer : IEqualityComparer<int>
            {
                public bool Equals(int left, int right) => left == right;

                public int GetHashCode(int value) => value * 2;
            }

            enum SignedStorage : long { Low = -9223372036854775807L, High = 9223372036854775807L }
            enum UnsignedStorage : ulong { Low = 1UL, High = 18446744073709551615UL }
                        public static class EntryPoint
            {
                public static int Run(int input)
                {
                    static bool RoundTrips<T>(T first, T second) where T : struct
                    {
                        var values = new object[64];
                        for (var index = 0; index < values.Length; index++)
                        {
                            values[index] = (index & 1) == 0 ? first : second;
                        }

                        for (var index = 0; index < values.Length; index++)
                        {
                            var expected = (index & 1) == 0 ? first : second;
                            if (!EqualityComparer<T>.Default.Equals((T)values[index], expected))
                            {
                                return false;
                            }
                        }

                        return values[0].GetHashCode() == values[2].GetHashCode();
                    }

                    if (!RoundTrips(false, true)) return 101;
                    if (!RoundTrips((sbyte)-128, (sbyte)127)) return 102;
                    if (!RoundTrips((byte)0, byte.MaxValue)) return 103;
                    if (!RoundTrips(short.MinValue, short.MaxValue)) return 104;
                    if (!RoundTrips((ushort)0, ushort.MaxValue)) return 105;
                    if (!RoundTrips('A', '\uffff')) return 106;
                    if (!RoundTrips(int.MinValue, int.MaxValue)) return 107;
                    if (!RoundTrips((uint)0, uint.MaxValue)) return 108;
                    if (!RoundTrips(long.MinValue, long.MaxValue)) return 109;
                    if (!RoundTrips((ulong)0, ulong.MaxValue)) return 110;
                    if (!RoundTrips(-1.25f, 3.5f)) return 111;
                    if (!RoundTrips(-1.25d, 3.5d)) return 112;
                    if (!RoundTrips((nint)(-17), (nint)29)) return 113;
                    if (!RoundTrips((nuint)17, (nuint)29)) return 114;
                    if (!RoundTrips(SignedStorage.Low, SignedStorage.High)) return 115;
                    if (!RoundTrips(UnsignedStorage.Low, UnsignedStorage.High)) return 116;
                    if (!RoundTrips((long.MinValue, ulong.MaxValue), (long.MaxValue, 0UL))) return 117;
                    object present = (long?)42;
                    object? absent = (long?)null;
                    if ((long)present != 42 || absent is not null) return 118;

                    var expected = new[]
                    {
                        HashCode.Combine(1),
                        HashCode.Combine(1, 2),
                        HashCode.Combine(1, 2, 3),
                        HashCode.Combine(1, 2, 3, 4),
                        HashCode.Combine(1, 2, 3, 4, 5),
                        HashCode.Combine(1, 2, 3, 4, 5, 6),
                        HashCode.Combine(1, 2, 3, 4, 5, 6, 7),
                        HashCode.Combine(1, 2, 3, 4, 5, 6, 7, 8),
                    };

                    for (var length = 1; length <= expected.Length; length++)
                    {
                        var hash = new HashCode();
                        for (var value = 1; value <= length; value++)
                        {
                            hash.Add(value);
                        }

                        if (hash.ToHashCode() != expected[length - 1] ||
                            hash.ToHashCode() != expected[length - 1])
                        {
                            return length;
                        }
                    }

                    var comparerHash = new HashCode();
                    comparerHash.Add(7, new DoubledHashComparer());
                    if (comparerHash.ToHashCode() != HashCode.Combine(14)) return 20;
                    if (HashCode.Combine<string>(null) != HashCode.Combine(0)) return 21;
                    if (HashCode.Combine(1, 2) == HashCode.Combine(2, 1)) return 22;

                    try
                    {
                        _ = new HashCode().GetHashCode();
                        return 30;
                    }
                    catch (NotSupportedException exception)
                    {
                        if (exception.Message !=
                            "HashCode is a mutable struct and should not be compared with other HashCodes. Use ToHashCode to retrieve the computed hash code.")
                        {
                            return 31;
                        }
                    }

                    try
                    {
                        _ = new HashCode().Equals(null);
                        return 32;
                    }
                    catch (NotSupportedException exception)
                    {
                        if (exception.Message !=
                            "HashCode is a mutable struct and should not be compared with other HashCodes.")
                        {
                            return 33;
                        }
                    }

                    return input - input;
                }
            }
            #pragma warning restore CS0618
            """;
        ICompilationScenarioExecutor executor = new CompilationScenarioExecutor(assets);
        var result = executor.Execute(new CompilationScenario(
            $"HashCodeFixture_{optimize}_{target}",
            source,
            "HashCodeFixture.EntryPoint",
            optimize,
            target,
            41,
            []));

        Assert.Equal(0, result);
    }
}
