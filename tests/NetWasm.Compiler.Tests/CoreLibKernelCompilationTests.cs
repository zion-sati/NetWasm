// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

using static CompilerTestSupport;

public sealed class CoreLibKernelCompilationTests
{
    [Fact]
    public void CompilerExecutesGenericBucketAndEntryKernel()
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource(
            "CollectionKernelFixture",
            """
            namespace CollectionKernelFixture;

            public interface IKeyComparer<T>
            {
                int Hash(T value);
                bool Equals(T left, T right);
            }

            public sealed class IntComparer : IKeyComparer<int>
            {
                public int Hash(int value) => value;
                public bool Equals(int left, int right) => left == right;
            }

            public sealed class Key
            {
                public Key(int value) => Value = value;
                public int Value;
            }

            public sealed class KeyComparer : IKeyComparer<Key>
            {
                public int Hash(Key value) => value.Value;
                public bool Equals(Key left, Key right) => left.Value == right.Value;
            }

            public struct Entry<T>
            {
                public int Hash;
                public int Next;
                public T Key;
                public int Value;
            }

            public sealed class Table<T>
            {
                private int[] _buckets;
                private Entry<T>[] _entries;
                private int _count;
                private readonly IKeyComparer<T> _comparer;

                public Table(IKeyComparer<T> comparer)
                {
                    _comparer = comparer;
                    _buckets = new int[2];
                    _entries = new Entry<T>[2];
                }

                public void Add(T key, int value)
                {
                    if (_count == _entries.Length)
                    {
                        Resize();
                    }
                    var hash = _comparer.Hash(key) & int.MaxValue;
                    var bucket = hash % _buckets.Length;
                    _entries[_count].Hash = hash;
                    _entries[_count].Next = _buckets[bucket] - 1;
                    _entries[_count].Key = key;
                    _entries[_count].Value = value;
                    _buckets[bucket] = _count + 1;
                    _count++;
                }

                public bool TryGetValue(T key, out int value)
                {
                    var hash = _comparer.Hash(key) & int.MaxValue;
                    value = 0;
                    var found = false;
                    var index = _buckets[hash % _buckets.Length] - 1;
                    while (index >= 0)
                    {
                        if (_entries[index].Hash == hash &&
                            _comparer.Equals(_entries[index].Key, key))
                        {
                            value = _entries[index].Value;
                            found = true;
                            index = -1;
                        }
                        else
                        {
                            index = _entries[index].Next;
                        }
                    }
                    return found;
                }

                private void Resize()
                {
                    var entries = new Entry<T>[_entries.Length * 2];
                    for (var index = 0; index < _count; index++)
                    {
                        entries[index] = _entries[index];
                    }
                    _entries = entries;
                    _buckets = new int[entries.Length];
                    for (var index = 0; index < _count; index++)
                    {
                        var bucket = _entries[index].Hash % _buckets.Length;
                        _entries[index].Next = _buckets[bucket] - 1;
                        _buckets[bucket] = index + 1;
                    }
                }
            }

            public static class EntryPoint
            {
                public static int Run(int input)
                {
                    var numbers = new Table<int>(new IntComparer());
                    numbers.Add(1, input);
                    numbers.Add(3, 1);
                    numbers.Add(5, 2);
                    var keys = new Table<Key>(new KeyComparer());
                    keys.Add(new Key(7), 4);
                    return numbers.TryGetValue(1, out var first) &&
                           numbers.TryGetValue(5, out var second) &&
                           keys.TryGetValue(new Key(7), out var third)
                        ? first + second + third
                        : -1;
                }
            }
            """);

        var result = NetWasmCompiler.Compile(new CompilerOptions(
            assembly,
            [assets.CoreLib],
            "CollectionKernelFixture.EntryPoint",
            "Run",
            []));

        Assert.Equal(47, ExecuteWithNode(result.ApplicationModule, assets.Directory, 41));
    }

    [Fact]
    public void CompilerExecutesFixedWidthDecimalArithmeticKernel()
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource(
            "DecimalKernelFixture",
            """
            namespace DecimalKernelFixture;

            public struct UInt96
            {
                public uint Low;
                public uint Middle;
                public uint High;

                public static UInt96 Add(UInt96 left, UInt96 right)
                {
                    var low = (ulong)left.Low + right.Low;
                    var middle = (ulong)left.Middle + right.Middle + (low >> 32);
                    var high = (ulong)left.High + right.High + (middle >> 32);
                    return new UInt96
                    {
                        Low = (uint)low,
                        Middle = (uint)middle,
                        High = (uint)high,
                    };
                }
            }

            public struct UInt192
            {
                public uint Part0;
                public uint Part1;
                public uint Part2;
                public uint Part3;
                public uint Part4;
                public uint Part5;

                public static UInt192 Multiply(uint left, uint right)
                {
                    var product = (ulong)left * right;
                    return new UInt192
                    {
                        Part0 = (uint)product,
                        Part1 = (uint)(product >> 32),
                    };
                }
            }

            public static class EntryPoint
            {
                public static int Run(int input)
                {
                    var sum = UInt96.Add(
                        new UInt96 { Low = uint.MaxValue, Middle = (uint)input },
                        new UInt96 { Low = 2, Middle = 1 });
                    var product = UInt192.Multiply((uint)input, 100_000_000U);
                    var scaled = ((ulong)product.Part1 << 32) | product.Part0;
                    var quotient = scaled / 10_000_000UL;
                    var remainder = scaled % 10_000_000UL;
                    return (int)sum.Low + (int)sum.Middle + (int)sum.High
                        + (int)quotient + (int)remainder;
                }
            }
            """);

        var result = NetWasmCompiler.Compile(new CompilerOptions(
            assembly,
            [assets.CoreLib],
            "DecimalKernelFixture.EntryPoint",
            "Run",
            []));

        Assert.Equal(454, ExecuteWithNode(result.ApplicationModule, assets.Directory, 41));
    }

    [Fact]
    public void CompilerTreatsProductionDecimalLiteralConstructionAsOrdinaryManagedCode()
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource(
            "DecimalLiteralShapeFixture",
            """

            namespace DecimalLiteralShapeFixture
            {
                public static class EntryPoint
                {
                    public static int Run(int input)
                    {
                        decimal value = 12.5m;
                        var bits = decimal.GetBits(value);
                        return bits[0] + bits[1] + bits[2] + bits[3] + input;
                    }
                }
            }
            """);

        var result = NetWasmCompiler.Compile(new CompilerOptions(
            assembly,
            [assets.CoreLib],
            "DecimalLiteralShapeFixture.EntryPoint",
            "Run",
            []));
        var instructions = result.Program.Methods.Values
            .SelectMany(method => method.Body.Instructions)
            .ToArray();

        Assert.Contains(instructions, instruction =>
            instruction.Operation == CilOperation.Call);
        Assert.Equal(65702, ExecuteWithNode(result.ApplicationModule, assets.Directory, 41));
    }
}
