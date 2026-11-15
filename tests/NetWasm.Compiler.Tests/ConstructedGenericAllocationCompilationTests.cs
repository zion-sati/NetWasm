// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

using static CompilerTestSupport;

public sealed class ConstructedGenericAllocationCompilationTests
{
    [Fact]
    public void CompilerPreservesAnAllocatingConstructedGenericInstanceAfterConstruction()
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource(
            "ConstructedGenericAllocationFixture",
            """
            namespace ConstructedGenericAllocationFixture;

            public sealed class Container<T>
            {
                private readonly int[] _buckets;
                private readonly Entry[] _items;

                public Container()
                {
                    _buckets = new int[1];
                    _items = new Entry[1];
                    _items[0] = new Entry(default!);
                }

                public int Count => _buckets.Length + _items.Length;

                private readonly struct Entry
                {
                    internal readonly T Value;
                    internal Entry(T value) => Value = value;
                }
            }

            public static class EntryPoint
            {
                public static int Run(int input)
                {
                    var container = new Container<string>();
                    return container.Count == 2 ? input : -1;
                }
            }
            """);
        var compilation = NetWasmCompiler.Compile(new CompilerOptions(
            assembly,
            [assets.CoreLib],
            "ConstructedGenericAllocationFixture.EntryPoint",
            "Run",
            []));

        Assert.Equal(41, ExecuteWithNode(
            compilation.ApplicationModule,
            assets.Directory,
            41));
    }

    [Fact]
    public void CompilerConstructsAClosedDictionaryBeforeItsFirstMutation()
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource(
            "DictionaryConstructionFixture",
            """
            using System.Collections.Generic;

            namespace DictionaryConstructionFixture;

            public sealed class Comparer : IEqualityComparer<int>
            {
                public bool Equals(int left, int right) => left == right;
                public int GetHashCode(int value) => 1;
            }

            public static class EntryPoint
            {
                public static int Run(int input)
                {
                    var dictionary = new Dictionary<int, string>(1, new Comparer());
                    return dictionary.Count == 0 ? input : -1;
                }
            }
            """);
        var compilation = NetWasmCompiler.Compile(new CompilerOptions(
            assembly,
            [assets.CoreLib],
            "DictionaryConstructionFixture.EntryPoint",
            "Run",
            []));

        Assert.Equal(41, ExecuteWithNode(
            compilation.ApplicationModule,
            assets.Directory,
            41));
    }

    [Fact]
    public void CompilerExecutesTheFirstClosedDictionaryInsertion()
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource(
            "DictionaryInsertionFixture",
            """
            using System.Collections.Generic;

            namespace DictionaryInsertionFixture;

            public sealed class Comparer : IEqualityComparer<int>
            {
                public bool Equals(int left, int right) => left == right;
                public int GetHashCode(int value) => 1;
            }

            public static class EntryPoint
            {
                public static int Run(int input)
                {
                    var dictionary = new Dictionary<int, string>(1, new Comparer());
                    dictionary.Add(1, "one");
                    return dictionary.Count == 1 ? input : -1;
                }
            }
            """);
        var compilation = NetWasmCompiler.Compile(new CompilerOptions(
            assembly,
            [assets.CoreLib],
            "DictionaryInsertionFixture.EntryPoint",
            "Run",
            []));

        Assert.Equal(41, ExecuteWithNode(
            compilation.ApplicationModule,
            assets.Directory,
            41));
    }

    [Fact]
    public void CompilerExecutesClosedDictionaryGrowthAndCollisions()
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource(
            "DictionaryGrowthFixture",
            """
            using System.Collections.Generic;

            namespace DictionaryGrowthFixture;

            public sealed class Comparer : IEqualityComparer<int>
            {
                public bool Equals(int left, int right) => left == right;
                public int GetHashCode(int value) => 1;
            }

            public static class EntryPoint
            {
                public static int Run(int input)
                {
                    var dictionary = new Dictionary<int, string>(1, new Comparer());
                    for (var index = 0; index < input; index++)
                        dictionary.Add(index, "value");
                    return dictionary.Count;
                }
            }
            """);
        var compilation = NetWasmCompiler.Compile(new CompilerOptions(
            assembly,
            [assets.CoreLib],
            "DictionaryGrowthFixture.EntryPoint",
            "Run",
            []));

        foreach (var count in new[] { 1, 4, 5, 8, 9, 16, 17, 24 })
        {
            Assert.Equal(count, ExecuteWithNode(
                compilation.ApplicationModule,
                assets.Directory,
                count));
        }
    }

    [Fact]
    public void CompilerHonorsAnEarlyReturnBeforeDictionaryEnumeration()
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource(
            "DictionaryEarlyReturnFixture",
            """
            using System.Collections.Generic;

            namespace DictionaryEarlyReturnFixture;

            public static class EntryPoint
            {
                public static int Run(int input)
                {
                    var dictionary = new Dictionary<int, string>();
                    dictionary.Add(1, "one");
                    if (input == 11)
                        return 11;
                    var total = 0;
                    foreach (var pair in dictionary)
                        total += pair.Key;
                    return total;
                }
            }
            """);
        var compilation = NetWasmCompiler.Compile(new CompilerOptions(
            assembly,
            [assets.CoreLib],
            "DictionaryEarlyReturnFixture.EntryPoint",
            "Run",
            []));
        Assert.Equal(11, ExecuteWithNode(
            compilation.ApplicationModule,
            assets.Directory,
            11));
    }

    [Fact]
    public void CompilerEnumeratesAnSzArrayThroughGenericIEnumerable()
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource(
            "ArrayEnumerableFixture",
            """
            using System.Collections.Generic;

            namespace ArrayEnumerableFixture;

            public static class EntryPoint
            {
                public static int Run(int input)
                {
                    IEnumerable<int> values = new[] { 1, 2, input };
                    var total = 0;
                    foreach (var value in values)
                        total += value;
                    return total;
                }
            }
            """);
        var compilation = NetWasmCompiler.Compile(new CompilerOptions(
            assembly,
            [assets.CoreLib],
            "ArrayEnumerableFixture.EntryPoint",
            "Run",
            []));

        Assert.Equal(44, ExecuteWithNode(
            compilation.ApplicationModule,
            assets.Directory,
            41));
    }
}
