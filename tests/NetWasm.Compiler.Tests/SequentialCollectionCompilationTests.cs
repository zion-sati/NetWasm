// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

using static CompilerTestSupport;

public sealed class SequentialCollectionCompilationTests
{
    [Fact]
    public void CompilerExecutesMutableAndReadOnlyCollectionWrappers()
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource(
            "CollectionWrapperFixture",
            """
            using System.Collections.Generic;
            using System.Collections.ObjectModel;

            namespace CollectionWrapperFixture;

            public static class EntryPoint
            {
                public static int Run(int input)
                {
                    var storage = new List<int>();
                    var mutable = new Collection<int>(storage);
                    mutable.Add(input);
                    mutable.Insert(0, 1);
                    var readOnly = new ReadOnlyCollection<int>(storage);
                    var rejected = false;
                    try
                    {
                        ((ICollection<int>)readOnly).Add(2);
                    }
                    catch (System.NotSupportedException)
                    {
                        rejected = true;
                    }

                    var dictionary = new Dictionary<int, int>();
                    dictionary.Add(1, input);
                    var readOnlyDictionary = new ReadOnlyDictionary<int, int>(dictionary);
                    return rejected && readOnly.Count == 2
                        && readOnlyDictionary.TryGetValue(1, out var value)
                        ? readOnly[0] + readOnly[1] + value
                        : -1;
                }
            }
            """);

        var result = NetWasmCompiler.Compile(new CompilerOptions(
            assembly,
            [assets.CoreLib],
            "CollectionWrapperFixture.EntryPoint",
            "Run",
            []));

        Assert.Equal(83, ExecuteWithNode(result.ApplicationModule, assets.Directory, 41));
    }

    [Fact]
    public void CompilerExecutesLinkedListNodesOwnershipAndFailFastEnumeration()
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource(
            "LinkedListContractFixture",
            """
            using System.Collections.Generic;

            namespace LinkedListContractFixture;

            public static class EntryPoint
            {
                public static int Run(int input)
                {
                    var list = new LinkedList<int>();
                    var first = list.AddFirst(1);
                    var last = list.AddLast(input);
                    list.AddAfter(first, 2);
                    list.AddBefore(last, 3);
                    var ordering = list.First!.Value * 1000
                        + list.First.Next!.Value * 100
                        + list.Last!.Previous!.Value * 10
                        + list.Last.Value;
                    var enumerator = list.GetEnumerator();
                    enumerator.MoveNext();
                    list.Remove(first);
                    var failedFast = false;
                    try
                    {
                        enumerator.MoveNext();
                    }
                    catch (System.InvalidOperationException)
                    {
                        failedFast = true;
                    }
                    var detached = first.List == null
                        && first.Next == null
                        && first.Previous == null;
                    return failedFast && detached && list.Find(3) != null
                        ? ordering
                        : -1;
                }
            }
            """);

        var result = NetWasmCompiler.Compile(new CompilerOptions(
            assembly,
            [assets.CoreLib],
            "LinkedListContractFixture.EntryPoint",
            "Run",
            []));

        Assert.Equal(1271, ExecuteWithNode(result.ApplicationModule, assets.Directory, 41));
    }

    [Fact]
    public void CompilerSortsArraysAndListsWithDefaultAndCustomComparers()
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource(
            "SortContractFixture",
            """
            using System.Collections.Generic;

            namespace SortContractFixture;

            public sealed class Descending : IComparer<int>
            {
                public int Compare(int left, int right) => right.CompareTo(left);
            }

            public static class EntryPoint
            {
                public static int Run(int input)
                {
                    int[] values = [3, input, 1, 2];
                    System.Array.Sort(values);
                    var list = new List<int>();
                    for (var index = 0; index < values.Length; index++)
                    {
                        list.Add(values[index]);
                    }
                    list.Sort(new Descending());
                    return values[0] + values[3] + list[0] + list[3];
                }
            }
            """);

        var result = NetWasmCompiler.Compile(new CompilerOptions(
            assembly,
            [assets.CoreLib],
            "SortContractFixture.EntryPoint",
            "Run",
            []));

        Assert.Equal(84, ExecuteWithNode(result.ApplicationModule, assets.Directory, 41));
    }

    [Fact]
    public void CompilerExecutesArrayOverlapResizeAndClear()
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource(
            "ArrayContractFixture",
            """
            namespace ArrayContractFixture;

            public static class EntryPoint
            {
                public static int Run(int input)
                {
                    int[]? values = [input, 1, 2, 3];
                    System.Array.Copy(values, 0, values, 1, 3);
                    System.Array.Resize(ref values, 6);
                    System.Array.Clear(values, 4, 2);
                    return values[0] + values[1] + values[2] + values[3]
                        + values[4] + values[5]
                        + System.Array.IndexOf(values, 1);
                }
            }
            """);

        var result = NetWasmCompiler.Compile(new CompilerOptions(
            assembly,
            [assets.CoreLib],
            "ArrayContractFixture.EntryPoint",
            "Run",
            []));

        Assert.Equal(87, ExecuteWithNode(result.ApplicationModule, assets.Directory, 41));
    }

    [Fact]
    public void CompilerExecutesListQueueStackAndFailFastEnumeration()
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource(
            "SequentialCollectionFixture",
            """
            using System.Collections.Generic;

            namespace SequentialCollectionFixture;

            public static class EntryPoint
            {
                public static int Run(int input)
                {
                    var list = new List<int>();
                    list.Add(input);
                    list.Add(1);
                    list.AddRange(list.ToArray());
                    var enumerator = list.GetEnumerator();
                    enumerator.MoveNext();
                    list[0] = input + 1;
                    var failedFast = false;
                    try
                    {
                        enumerator.MoveNext();
                    }
                    catch (System.InvalidOperationException)
                    {
                        failedFast = true;
                    }

                    var queue = new Queue<int>();
                    var stack = new Stack<int>();
                    for (var index = 0; index < list.Count; index++)
                    {
                        queue.Enqueue(list[index]);
                        stack.Push(list[index]);
                    }
                    var queueValue = queue.Dequeue() + queue.Peek();
                    var stackValue = stack.Pop() + stack.Peek();
                    return failedFast ? queueValue + stackValue : -1;
                }
            }
            """);

        var result = NetWasmCompiler.Compile(new CompilerOptions(
            assembly,
            [assets.CoreLib],
            "SequentialCollectionFixture.EntryPoint",
            "Run",
            []));

        Assert.Equal(85, ExecuteWithNode(result.ApplicationModule, assets.Directory, 41));
    }

    [Fact]
    public void CompilerExecutesListRangeLookupRemovalAndCapacitySurface()
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource(
            "ListRangeContractFixture",
            """
            using System.Collections.Generic;

            namespace ListRangeContractFixture;

            public static class EntryPoint
            {
                public static int Run(int input)
                {
                    var list = new List<int>(new[] { 1, 2, 3, 2, input });
                    list.InsertRange(2, new[] { 8, 9 });
                    var range = list.GetRange(1, 4);
                    var firstEven = range.Find(static value => value % 2 == 0);
                    var even = range.FindAll(static value => value % 2 == 0);
                    var removed = list.RemoveAll(static value => value == 2);
                    list.RemoveRange(1, 2);
                    list.AddRange(list);
                    list.Reverse();
                    var capacity = list.EnsureCapacity(20);
                    list.TrimExcess();
                    return firstEven == 2 && even.Count == 2 && removed == 2
                        && capacity >= 20 && list.Capacity == list.Count
                        && list.LastIndexOf(input) >= 0
                        ? list.Count * 100 + list[0]
                        : -1;
                }
            }
            """);

        var result = NetWasmCompiler.Compile(new CompilerOptions(
            assembly,
            [assets.CoreLib],
            "ListRangeContractFixture.EntryPoint",
            "Run",
            []));

        Assert.Equal(641, ExecuteWithNode(result.ApplicationModule, assets.Directory, 41));
    }
}
