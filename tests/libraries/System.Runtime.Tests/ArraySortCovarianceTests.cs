using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using TUnit.Assertions;
using TUnit.Core;

namespace NetWasm.CoreLib.Tests;

// Sorting a covariant array, a string[] reached as an object[] or as an Array, only permutes its own elements, so
// it succeeds as on desktop .NET. log4net sorts a string[] through Array.Sort(Array, int, int, IComparer).
public sealed class ArraySortCovarianceTests
{
    [Test]
    public async Task NonGenericSortOfAStringArraySortsIt()
    {
        string[] names = ["ccc", "a", "bb"];
        Array.Sort(names, 0, names.Length, LongestFirst.Instance);

        await Assert.That(string.Join(",", names)).IsEqualTo("ccc,bb,a");
    }

    [Test]
    public async Task NonGenericSortOfStringKeysAndItemsSortsBoth()
    {
        string[] keys = ["ccc", "a", "bb"];
        string[] items = ["3", "1", "2"];
        Array.Sort(keys, items, LongestFirst.Instance);

        await Assert.That(string.Join(",", keys)).IsEqualTo("ccc,bb,a");
        await Assert.That(string.Join(",", items)).IsEqualTo("3,2,1");
    }

    [Test]
    public async Task GenericSortOfAStringArrayAsObjectsSortsIt()
    {
        object[] byComparer = new string[] { "ccc", "a", "bb" };
        Array.Sort(byComparer, LongestFirst.Instance);
        object[] byComparison = new string[] { "ccc", "a", "bb" };
        Array.Sort(byComparison, LongestFirst.Instance.Compare);
        object[] keys = new string[] { "ccc", "a", "bb" };
        object[] items = new string[] { "3", "1", "2" };
        Array.Sort<object, object>(keys, items, LongestFirst.Instance);

        await Assert.That(string.Join(",", byComparer)).IsEqualTo("ccc,bb,a");
        await Assert.That(string.Join(",", byComparison)).IsEqualTo("ccc,bb,a");
        await Assert.That(string.Join(",", keys)).IsEqualTo("ccc,bb,a");
        await Assert.That(string.Join(",", items)).IsEqualTo("3,2,1");
    }

    private sealed class LongestFirst : IComparer, IComparer<object>
    {
        public static readonly LongestFirst Instance = new();

        public int Compare(object? x, object? y) => ((string)y!).Length.CompareTo(((string)x!).Length);
    }
}
