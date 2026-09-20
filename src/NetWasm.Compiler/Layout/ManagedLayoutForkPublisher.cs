using System;
using System.Collections.Generic;
using System.Linq;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Layout;

internal sealed class ManagedLayoutForkPublisher(
    ManagedValueLayoutState original,
    IReadOnlyList<ManagedValueLayoutState> workers) : IManagedLayoutForkPublisher
{
    private readonly ManagedValueLayoutState _original = original ??
        throw new ArgumentNullException(nameof(original));
    private readonly IReadOnlyList<ManagedValueLayoutState> _workers = workers ??
        throw new ArgumentNullException(nameof(workers));
    private ManagedValueLayoutState _baseline = new(original);

    public void Publish()
    {
        if (!EquivalentState(_baseline, _original))
            throw new InvalidOperationException(
                "Shared layout state changed during parallel work.");

        var merged = new ManagedValueLayoutState(_baseline);
        foreach (var worker in _workers)
        {
            ValidateBaseline(_baseline, worker);
            MergeMap(_baseline.Fields, merged.Fields, worker.Fields);
            MergeMap(_baseline.ConstructedFields, merged.ConstructedFields,
                worker.ConstructedFields);
            MergeMap(_baseline.Values, merged.Values, worker.Values);
            merged.CompletedMetadataLayouts.UnionWith(
                worker.CompletedMetadataLayouts);
        }

        foreach (var type in merged.CompletedMetadataLayouts)
            if (!merged.Values.ContainsKey(type))
                throw new InvalidOperationException(
                    "Completed layout has no value layout.");

        ApplyAdditions(_baseline, merged, _original);
        foreach (var worker in _workers)
            ApplyAdditions(_baseline, merged, worker);
        _baseline = merged;
    }

    private static void ValidateBaseline(
        ManagedValueLayoutState baseline, ManagedValueLayoutState worker)
    {
        if (!ContainsUnchanged(baseline.Fields, worker.Fields) ||
            !ContainsUnchanged(baseline.ConstructedFields,
                worker.ConstructedFields) ||
            !ContainsUnchanged(baseline.Values, worker.Values) ||
            !baseline.CompletedMetadataLayouts.IsSubsetOf(
                worker.CompletedMetadataLayouts))
            throw new InvalidOperationException(
                "Worker removed or changed a baseline layout.");
    }

    private static bool EquivalentState(
        ManagedValueLayoutState left, ManagedValueLayoutState right) =>
        SameMap(left.Fields, right.Fields) &&
        SameMap(left.ConstructedFields, right.ConstructedFields) &&
        SameMap(left.Values, right.Values) &&
        left.CompletedMetadataLayouts.SetEquals(right.CompletedMetadataLayouts);

    private static bool SameMap<TKey, TValue>(
        IReadOnlyDictionary<TKey, TValue> left,
        IReadOnlyDictionary<TKey, TValue> right) where TKey : notnull =>
        left.Count == right.Count && ContainsUnchanged(left, right);

    private static bool ContainsUnchanged<TKey, TValue>(
        IReadOnlyDictionary<TKey, TValue> baseline,
        IReadOnlyDictionary<TKey, TValue> candidate) where TKey : notnull =>
        baseline.All(pair => candidate.TryGetValue(pair.Key, out var value) &&
            Equivalent(pair.Value, value));

    private static void MergeMap<TKey, TValue>(
        IReadOnlyDictionary<TKey, TValue> baseline,
        Dictionary<TKey, TValue> merged,
        IReadOnlyDictionary<TKey, TValue> worker) where TKey : notnull
    {
        foreach (var pair in worker)
        {
            if (baseline.ContainsKey(pair.Key)) continue;
            if (merged.TryGetValue(pair.Key, out var existing))
            {
                if (!Equivalent(existing, pair.Value))
                    throw new InvalidOperationException(
                        "Workers resolved conflicting layouts.");
            }
            else merged.Add(pair.Key, pair.Value);
        }
    }

    private static void ApplyAdditions(
        ManagedValueLayoutState baseline,
        ManagedValueLayoutState merged,
        ManagedValueLayoutState target)
    {
        AddMissing(baseline.Fields, merged.Fields, target.Fields);
        AddMissing(baseline.ConstructedFields, merged.ConstructedFields,
            target.ConstructedFields);
        AddMissing(baseline.Values, merged.Values, target.Values);
        target.CompletedMetadataLayouts.UnionWith(
            merged.CompletedMetadataLayouts);
    }

    private static void AddMissing<TKey, TValue>(
        IReadOnlyDictionary<TKey, TValue> baseline,
        IReadOnlyDictionary<TKey, TValue> merged,
        Dictionary<TKey, TValue> target) where TKey : notnull
    {
        foreach (var pair in merged)
            if (!baseline.ContainsKey(pair.Key))
                target.TryAdd(pair.Key, pair.Value);
    }

    private static bool Equivalent<T>(T left, T right) =>
        left is ValueLayout first && right is ValueLayout second
            ? first.Type.Equals(second.Type) && first.Size == second.Size &&
                first.Alignment == second.Alignment &&
                first.ReferenceOffsets.SequenceEqual(second.ReferenceOffsets)
            : EqualityComparer<T>.Default.Equals(left, right);
}
