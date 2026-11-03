using System;
using System.Collections.Generic;
using System.Linq;

namespace NetWasm.Compiler.Layout;

// Numbers types in preorder of their base types: each type, then the types
// derived from it, so a type's descendants hold the ids from just after its
// own through its last subtype's, wherever the compiler first met them. A type
// test against a class then checks one interval instead of every allocated
// type derived from it.
internal static class TypeIdPreorder
{
    // Maps each id to its preorder number and its last descendant's. An id
    // whose base is zero or not among the ids is a root; siblings keep the
    // order of their ids.
    public static Dictionary<int, (int TypeId, int LastSubtypeId)> Number(IEnumerable<(int TypeId, int BaseTypeId)> types)
    {
        ArgumentNullException.ThrowIfNull(types);
        var bases = types.ToDictionary(type => type.TypeId, type => type.BaseTypeId);
        var derived = bases
            .Where(pair => pair.Value != 0)
            .GroupBy(pair => pair.Value, pair => pair.Key)
            .ToDictionary(group => group.Key, group => group.Order().ToArray());
        var numbers = new Dictionary<int, (int TypeId, int LastSubtypeId)>(bases.Count);
        var next = 1;
        // A stack rather than recursion: hierarchies may be deep.
        var pending = new Stack<(int TypeId, int Child)>();
        foreach (var root in bases.Where(pair => pair.Value == 0 || !bases.ContainsKey(pair.Value)).Select(pair => pair.Key).Order())
        {
            numbers.Add(root, (next++, 0));
            pending.Push((root, 0));
            while (pending.TryPop(out var visit))
            {
                if (derived.TryGetValue(visit.TypeId, out var children) && visit.Child < children.Length)
                {
                    pending.Push((visit.TypeId, visit.Child + 1));
                    numbers.Add(children[visit.Child], (next++, 0));
                    pending.Push((children[visit.Child], 0));
                    continue;
                }
                numbers[visit.TypeId] = numbers[visit.TypeId] with { LastSubtypeId = next - 1 };
            }
        }
        return numbers.Count == bases.Count
            ? numbers
            : throw new InvalidOperationException("the base types of the numbered types form a cycle");
    }
}
