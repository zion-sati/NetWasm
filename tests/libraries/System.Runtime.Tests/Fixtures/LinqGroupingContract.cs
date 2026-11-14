// From the compiler harness's LinqGroupingContractCompilationTests.cs: the same program, compiled once with the suite. On desktop .NET 11 it
// builds with DESKTOP_ORACLE defined.
#nullable disable
#pragma warning disable
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
namespace LinqGroupingFixture;

sealed class Group : IGrouping<string, string>
{
    private readonly string[] _items = ["a", "b"];
    public string Key => "k";
    public IEnumerator<string> GetEnumerator() => ((IEnumerable<string>)_items).GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

sealed class SingleLookup : ILookup<string, string>
{
    private readonly IGrouping<string, string> _group;
    public SingleLookup(IGrouping<string, string> group) => _group = group;
    public int Count => 1;
    public IEnumerable<string> this[string key] => key == _group.Key ? _group : Array.Empty<string>();
    public bool Contains(string key) => key == _group.Key;
    public IEnumerator<IGrouping<string, string>> GetEnumerator()
    {
        yield return _group;
    }
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

public static class EntryPoint
{
    public static int Run(int value)
    {
        IGrouping<string, string> group = new Group();
        IGrouping<object, object> covariant = group;
        var count = 0;
        foreach (var item in covariant)
        {
            count++;
        }
        if ((string)covariant.Key != "k" || count != 2) return 1;

        ILookup<string, string> lookup = new SingleLookup(group);
        var inLookup = 0;
        foreach (var item in lookup["k"])
        {
            inLookup++;
        }
        if (lookup.Count != 1 || !lookup.Contains("k") || lookup.Contains("x") || inLookup != 2) return 2;
        foreach (var grouping in lookup)
        {
            if (!ReferenceEquals(grouping, group)) return 3;
        }

        // Queryable's GroupBy builds its query tree (its MethodInfo folds from new Func<...>(GroupBy).Method).
        if (Queryable.GroupBy(Array.Empty<string>().AsQueryable(), s => s.Length).Expression is not
            System.Linq.Expressions.MethodCallExpression { Method.Name: "GroupBy" }) return 4;

        // PLINQ is upstream's: a null source is rejected before any query runs.
        try
        {
            _ = ParallelEnumerable.ToLookup<int, int>(null!, x => x);
            return 5;
        }
        catch (ArgumentNullException)
        {
        }

        return value + 1;
    }
}
