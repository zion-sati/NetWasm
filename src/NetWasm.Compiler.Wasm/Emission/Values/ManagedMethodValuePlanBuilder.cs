using System;
using System.Linq;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Wasm.Emission.Values;

internal sealed class ManagedMethodValuePlanBuilder(
    IManagedAggregateValueAnalyzer analyzer,
    IManagedAggregateSsaPlanner aggregates,
    IManagedAggregateRootMapProjector roots,
    ITransientAggregatePromotionPlanner transients) : IManagedMethodValuePlanBuilder
{
    private readonly IManagedAggregateValueAnalyzer _analyzer = analyzer ??
        throw new ArgumentNullException(nameof(analyzer));
    private readonly IManagedAggregateSsaPlanner _aggregates = aggregates ??
        throw new ArgumentNullException(nameof(aggregates));
    private readonly IManagedAggregateRootMapProjector _roots = roots ??
        throw new ArgumentNullException(nameof(roots));
    private readonly ITransientAggregatePromotionPlanner _transients = transients ??
        throw new ArgumentNullException(nameof(transients));

    public ManagedMethodValuePlan Build(ManagedMethodValuePlanRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Method);
        ArgumentNullException.ThrowIfNull(request.RootMap);
        ArgumentNullException.ThrowIfNull(request.ByRefEffects);
        ArgumentNullException.ThrowIfNull(request.ReservedStorages);
        if (!request.Enabled)
        {
            return ManagedMethodValuePlan.Unchanged(request.RootMap);
        }

        var analysis = _analyzer.Analyze(new(
            request.Method,
            request.RootMap,
            request.ByRefEffects,
            request.Enabled)
        {
            References = request.References,
            // A transient tuple local becomes values without storage.
            ExcludedLocals = _transients.Create(request.Method).PromotedLocalIndices,
        });
        var plan = _aggregates.Plan(new(request.Method, analysis) { References = request.References });
        if (!IsEmissionSupported(plan) ||
            plan.Storages.Keys.Any(request.ReservedStorages.Contains) ||
            !_roots.TryProject(new(request.RootMap, plan), out var rootMap))
        {
            return ManagedMethodValuePlan.Unchanged(request.RootMap);
        }
        return new(rootMap, plan);
    }

    private static bool IsEmissionSupported(ManagedAggregateSsaPlan plan) =>
        !plan.Storages.IsEmpty &&
        plan.Storages.All(pair =>
            pair.Key.Kind is ManagedAggregateStorageKind.Argument or ManagedAggregateStorageKind.Local &&
            pair.Value.Owned &&
            pair.Value.Size > 0 &&
            pair.Value.Size % sizeof(int) == 0 &&
            (pair.Key.Kind == ManagedAggregateStorageKind.Local ||
                plan.Materializations.Any(materialization =>
                    materialization.Materialization.Storage == pair.Key)) &&
            plan.Lanes.Count(lane => lane.Storage == pair.Key) ==
                pair.Value.Lanes.Count()) &&
        plan.Accesses.Values.All(access => access.Access.LaneOffsets.IsDefaultOrEmpty
            ? access.Access.Address.ByteOffset % sizeof(int) == 0 &&
                (access.Access.Size, access.Access.StackKind) is
                    (sizeof(int), CliValueKind.I4) or (sizeof(long), CliValueKind.I8) &&
                access.Reads.Length + access.Writes.Length == access.Access.Size / sizeof(int)
            : access.Reads.Length + access.Writes.Length == access.Access.LaneOffsets.Length &&
                (access.Access.StackKind != CliValueKind.ValueType || access.Reads.IsEmpty));
}
