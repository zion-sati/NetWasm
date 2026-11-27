// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using NetWasm.Compiler.ComponentModel.Optimization;
using NetWasm.Compiler.ControlFlow;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Analysis.ReceiverFlow;

// The receiver types a dispatched call's flow admitted: those that reached it, and those of its slot use's targets
// within the bounds of what reached it unfollowed (Bounded). Its targets are those of these receivers
// (RECEIVER_TYPE_FLOW_DESIGN.md, steps C and D).
internal sealed record ReceiverFlowAdmission(ImmutableHashSet<CliTypeIdentity> Receivers, bool Bounded);

// Holds a slot use's dispatch candidates until the receiver flow admits the receiver at one of its calls: type-based
// dispatch would reach the slot's method for every receiver its declaring type admits (for LINQ's Select over
// iterators a cross product of sources and selects; for object.ToString every constructed type's override).
internal sealed class ReceiverFlowDispatchGate(
    Func<CliTypeIdentity, FlowBound, bool> within,
    ReceiverFlowDispatchPolicy policy,
    bool passesUnknownSlots = false)
{
    private readonly Func<CliTypeIdentity, FlowBound, bool> _within = within ?? throw new ArgumentNullException(nameof(within));
    // Each held candidate with the order the closure filed it in, which releases keep.
    private readonly Dictionary<DispatchSlot, Dictionary<CliTypeIdentity, (DispatchCandidate Candidate, long Order)>> _held = [];
    private long _filed;
    private readonly Dictionary<DispatchSlot, HashSet<CliTypeIdentity>> _admitted = [];
    private readonly Dictionary<DispatchSlot, List<FlowBound>> _bounds = [];

    internal bool Gates(DispatchDeclaration declaration) =>
        Gates(policy, declaration.Operation, declaration.Declaration, declaration.ReceiverBound);

    // Whether a dispatched call of this method waits for the flow, whose targets therefore depend on other methods.
    // A call of a method that is not virtual, or is final, or constrained to a value type (the type picks the
    // implementation, and a value type is no object the flow follows), has one target whatever reaches it: nothing to
    // wait for.
    public static bool Gates(ReceiverFlowDispatchPolicy policy, CilOperation operation, MethodInstanceModel declaration,
        CliTypeIdentity? receiverBound) =>
        operation == CilOperation.CallVirtual && receiverBound is not { IsValueType: true } &&
        declaration.Definition.IsVirtual && !declaration.Definition.IsFinal &&
        (policy == ReceiverFlowDispatchPolicy.EveryVirtualCall || !declaration.MethodArguments.IsDefaultOrEmpty);

    // Whether the candidate waits for the flow; a candidate the flow already admits does not.
    internal bool Holds(DispatchCandidate candidate)
    {
        if (!Gates(candidate.Declaration) || Admits(candidate.Slot, candidate.Receiver)) return false;
        // A replayed gate admits by type a slot use the flow never saw.
        if (passesUnknownSlots && !_admitted.ContainsKey(candidate.Slot)) return false;
        if (!_held.TryGetValue(candidate.Slot, out var held)) _held.Add(candidate.Slot, held = []);
        held.TryAdd(candidate.Receiver, (candidate, _filed++));
        return true;
    }

    // The flow admits these receivers and bounds at the slot use: the candidates they release, in the order the closure
    // filed them, each with that order. The gate delays candidates and must not reorder them: the closure files them
    // level by level, as type-based dispatch queues them, and the generic cycle cutoff gives its breadth to the shallow
    // instances that come first (GenericCycleSessionFactory). Released by name, deep instantiations took that breadth.
    internal ImmutableArray<(DispatchCandidate Candidate, long Order)> Admit(DispatchSlot slot,
        IEnumerable<CliTypeIdentity> receivers, IEnumerable<FlowBound> bounds)
    {
        if (!_admitted.TryGetValue(slot, out var admitted)) _admitted.Add(slot, admitted = []);
        if (!_bounds.TryGetValue(slot, out var slotBounds)) _bounds.Add(slot, slotBounds = []);
        admitted.UnionWith(receivers);
        foreach (var bound in bounds)
        {
            if (!slotBounds.Contains(bound)) slotBounds.Add(bound);
        }
        if (!_held.TryGetValue(slot, out var held)) return [];
        var released = held.Where(pair => Admits(slot, pair.Key))
            .Select(static pair => pair.Value).OrderBy(static item => item.Order).ToImmutableArray();
        foreach (var item in released) held.Remove(item.Candidate.Receiver);
        return released;
    }

    private bool Admits(DispatchSlot slot, CliTypeIdentity receiver) =>
        _admitted.TryGetValue(slot, out var admitted) && admitted.Contains(receiver) ||
        _bounds.TryGetValue(slot, out var bounds) && bounds.Any(bound => _within(receiver, bound));
}

internal interface IClosureReceiverFlowFactory
{
    // The receiver flow for one closure build.
    ClosureReceiverFlow Create(ReachabilityLedger ledger, MethodDefinitionModel? entryPoint,
        IEnumerable<ProgramExport> exports, ReachabilityRoots roots);
}

// One per compile. The first closure build runs the flow; the builds after it (each reduction round's) replay what it
// admitted: a reduction only simplifies bodies, so the first build's admissions still cover every receiver, and a
// slot use the first build never saw dispatches by type.
internal sealed class ClosureReceiverFlowFactory(
    ReceiverFlowServices services,
    ReceiverFlowDispatchPolicy policy,
    bool failOnUnreachedBodies) : IClosureReceiverFlowFactory
{
    private readonly ReceiverFlowServices _services = services ?? throw new ArgumentNullException(nameof(services));
    private ClosureReceiverFlow? _first;
    private ClosureReceiverFlow.Admissions? _admissions;

    public ClosureReceiverFlow Create(ReachabilityLedger ledger, MethodDefinitionModel? entryPoint,
        IEnumerable<ProgramExport> exports, ReachabilityRoots roots)
    {
        if (_first is null && _admissions is null)
            return _first = new ClosureReceiverFlow(_services, ledger, entryPoint, exports, roots, policy,
                failOnUnreachedBodies);
        // The first build has finished: keep what it admitted, not its graph.
        if (_first is not null)
        {
            _admissions = _first.Admitted();
            _first = null;
        }
        return ClosureReceiverFlow.Replay(_admissions!, new ReceiverFlowGraph(_services.Relationships).Within, ledger,
            policy);
    }
}

// The receiver flow inside one closure build: at each quiescence the engine settles over the ledger, and the
// candidates its calls admit go back to the closure's queue.
internal sealed class ClosureReceiverFlow
{
    private readonly ReceiverFlowEngine? _engine;
    private readonly ReachabilityLedger _ledger;
    // What a replay admits: the first build's slot admissions and call admissions.
    private readonly Dictionary<DispatchSlot, (HashSet<CliTypeIdentity> Receivers, List<FlowBound> Bounds)> _slots = [];
    private readonly IReadOnlyDictionary<string, MethodCalls>? _replayedMethods;

    private readonly bool _failOnUnreachedBodies;

    // failOnUnreachedBodies: a qualification build (the receiver flow audit) fails where the closure compiled a method
    // the flow did not reach, so each such path gets modeled; other builds make those methods boundaries.
    public ClosureReceiverFlow(ReceiverFlowServices services, ReachabilityLedger ledger, MethodDefinitionModel? entryPoint,
        IEnumerable<ProgramExport> exports, ReachabilityRoots roots, ReceiverFlowDispatchPolicy policy,
        bool failOnUnreachedBodies)
    {
        ArgumentNullException.ThrowIfNull(services);
        // Only where the flow gates every virtual call does the closure compile nothing the flow did not reach: under
        // generic virtual calls alone, the other calls reach their targets by type, which the flow need not follow.
        _failOnUnreachedBodies = failOnUnreachedBodies && policy == ReceiverFlowDispatchPolicy.EveryVirtualCall;
        _ledger = ledger ?? throw new ArgumentNullException(nameof(ledger));
        _engine = new ReceiverFlowEngine(services, new ReachabilityLedgerFlowView(ledger, entryPoint, exports, roots));
        Gate = new ReceiverFlowDispatchGate(_engine.Graph.Within, policy);
    }

    private ClosureReceiverFlow(Admissions admissions, Func<CliTypeIdentity, FlowBound, bool> within,
        ReachabilityLedger ledger, ReceiverFlowDispatchPolicy policy)
    {
        _ledger = ledger;
        Gate = new ReceiverFlowDispatchGate(within, policy, passesUnknownSlots: true);
        foreach (var (slot, (receivers, bounds)) in admissions.Slots) Gate.Admit(slot, receivers, bounds);
        _replayedMethods = admissions.Methods;
    }

    public ReceiverFlowDispatchGate Gate { get; }

    // What a finished build admitted: by slot use, and for each method its gated calls (offset and declaration) with
    // each call's admission.
    internal sealed record Admissions(
        IReadOnlyDictionary<DispatchSlot, (HashSet<CliTypeIdentity> Receivers, List<FlowBound> Bounds)> Slots,
        IReadOnlyDictionary<string, MethodCalls> Methods);

    internal sealed record MethodCalls(
        ImmutableHashSet<(int Offset, string Declaration)> Calls,
        IReadOnlyDictionary<string, ReceiverFlowAdmission> Sites);

    internal Admissions Admitted()
    {
        var sites = SiteAdmissions();
        var methods = new Dictionary<string, MethodCalls>(StringComparer.Ordinal);
        foreach (var group in GatedCalls(_ledger).GroupBy(static call => call.Declaration.Caller, StringComparer.Ordinal))
        {
            methods.Add(group.Key, new(
                group.Select(static call => (call.Declaration.IlOffset, call.Declaration.Declaration.CanonicalName))
                    .ToImmutableHashSet(),
                group.Where(call => sites.ContainsKey(call.Site))
                    .ToDictionary(static call => call.Site, call => sites[call.Site], StringComparer.Ordinal)));
        }
        return new(_slots, methods);
    }

    private IEnumerable<(string Site, DispatchDeclaration Declaration)> GatedCalls(ReachabilityLedger ledger) =>
        ledger.DispatchDeclarations.Where(pair => Gate.Gates(pair.Value)).Select(static pair => (pair.Key, pair.Value));

    // A later build's flow: a finished build's admissions, without running the engine again.
    public static ClosureReceiverFlow Replay(Admissions admissions, Func<CliTypeIdentity, FlowBound, bool> within,
        ReachabilityLedger ledger, ReceiverFlowDispatchPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(admissions);
        ArgumentNullException.ThrowIfNull(within);
        ArgumentNullException.ThrowIfNull(ledger);
        return new ClosureReceiverFlow(admissions, within, ledger, policy);
    }

    // Settles the flow over what the ledger holds and queues the candidates it admits; the count queued.
    internal int Settle()
    {
        if (_engine is null) return 0;
        _engine.ReachRoots();
        _engine.Settle();
        var released = Release();
        if (released > 0) return released;
        // Only once the flow releases nothing more has it caught up with the closure: before that, a call that waits
        // for no flow (a final or non-virtual method, a constrained value type) can have the closure compile a callee
        // the flow reaches only after it releases what leads there.
        var unreached = _engine.ReachUnreachedBodies();
        if (unreached.Length == 0) return 0;
        if (_failOnUnreachedBodies)
        {
            throw new CompilerException(new CompilerDiagnostic(DiagnosticCode.CompilerInvariant,
                $"receiver flow audit: the closure compiled {unreached.Length} methods the flow did not reach, " +
                $"by paths it does not model: {string.Join(", ", unreached.Take(8))}"));
        }
        _engine.Settle();
        return Release();
    }

    // Queues the candidates of what the flow admitted at each gated call; the count queued.
    private int Release()
    {
        var released = 0;
        var releases = new List<(DispatchCandidate Candidate, long Order)>();
        // The queue order feeds the program's ids and the generic cycle cutoff: the released candidates go in the order
        // the closure filed them, which is fixed, not in the engine's order, which follows the order it reached methods
        // in and which hashing can change between runs. Slot uses are visited in a fixed order only for that reason.
        foreach (var group in GatedAdmissions().GroupBy(static admission => admission.Slot)
            .OrderBy(static group => group.Key.Declaration, StringComparer.Ordinal)
            .ThenBy(static group => group.Key.Operation))
        {
            var receivers = group.SelectMany(static admission => admission.Receivers).ToArray();
            var bounds = group.SelectMany(static admission => admission.Bounds).ToArray();
            if (!_slots.TryGetValue(group.Key, out var slot)) _slots.Add(group.Key, slot = ([], []));
            slot.Receivers.UnionWith(receivers);
            foreach (var bound in bounds)
            {
                if (!slot.Bounds.Contains(bound)) slot.Bounds.Add(bound);
            }
            releases.AddRange(Gate.Admit(group.Key, receivers, bounds));
        }
        foreach (var (candidate, _) in releases.OrderBy(static item => item.Order))
        {
            _ledger.PendingDispatches.Enqueue((candidate.Slot, candidate.Declaration, candidate.Receiver));
            released++;
        }
        return released;
    }

    // Each dispatched call's admission, for the program builder. A replay gives a method's calls the first
    // build's admissions only where its gated calls are exactly the first build's: a reduction rewrites bodies, and
    // inlining after it can move a callee's call into its caller, so a call that is not where it was takes its slot
    // use's targets (the union the gate admitted).
    internal ImmutableDictionary<string, ReceiverFlowAdmission> SiteAdmissions()
    {
        if (_replayedMethods is not null)
        {
            var replayed = ImmutableDictionary.CreateBuilder<string, ReceiverFlowAdmission>(StringComparer.Ordinal);
            foreach (var group in GatedCalls(_ledger).GroupBy(static call => call.Declaration.Caller, StringComparer.Ordinal))
            {
                if (!_replayedMethods.TryGetValue(group.Key, out var first) ||
                    !first.Calls.SetEquals(group.Select(static call =>
                        (call.Declaration.IlOffset, call.Declaration.Declaration.CanonicalName))))
                {
                    continue;
                }
                foreach (var (site, _) in group)
                {
                    if (first.Sites.TryGetValue(site, out var admission)) replayed.Add(site, admission);
                }
            }
            return replayed.ToImmutable();
        }
        var result = ImmutableDictionary.CreateBuilder<string, ReceiverFlowAdmission>(StringComparer.Ordinal);
        foreach (var (site, slot, receivers, bounds) in GatedAdmissions())
        {
            var admitted = ImmutableHashSet.CreateBuilder<CliTypeIdentity>();
            admitted.UnionWith(receivers);
            if (bounds.Count > 0 && _ledger.DispatchTargets.TryGetValue(slot, out var targets))
            {
                foreach (var target in targets.Values)
                {
                    if (bounds.Any(bound => _engine!.Graph.Within(target.ReceiverType, bound))) admitted.Add(target.ReceiverType);
                }
            }
            result.Add(site, new ReceiverFlowAdmission(admitted.ToImmutable(), bounds.Count > 0));
        }
        return result.ToImmutable();
    }

    private IEnumerable<(string Site, DispatchSlot Slot, IReadOnlySet<CliTypeIdentity> Receivers,
        IReadOnlyList<FlowBound> Bounds)> GatedAdmissions()
    {
        foreach (var (site, _, arrived, bounds) in _engine!.Admissions())
        {
            if (!_ledger.DispatchDeclarations.TryGetValue(site, out var declaration) ||
                !Gate.Gates(declaration))
            {
                continue;
            }
            yield return (site, new DispatchSlot(declaration.Operation, declaration.Declaration.CanonicalName), arrived, bounds);
        }
    }
}

// A closure's ledger as the engine reads it, again at each settle.
internal sealed class ReachabilityLedgerFlowView(
    ReachabilityLedger ledger,
    MethodDefinitionModel? entryPoint,
    IEnumerable<ProgramExport> exports,
    ReachabilityRoots roots) : IReceiverFlowProgramView
{
    private readonly ReachabilityLedger _ledger = ledger ?? throw new ArgumentNullException(nameof(ledger));
    private readonly ImmutableArray<EntityKey> _rootKeys = [
        .. (roots ?? throw new ArgumentNullException(nameof(roots))).Methods,
        .. roots.InvokedMethods,
        .. (exports ?? throw new ArgumentNullException(nameof(exports))).Select(static export => export.Method),
        .. entryPoint is null ? [] : new[] { entryPoint.Key },
    ];
    private readonly LedgerBodies _bodies = new(ledger);

    public IEntryLookup<string, ManagedMethodBody> MethodBodies => _bodies;

    public IEnumerable<ManagedMethodBody> Bodies => _bodies.Current.Values;

    public IMembership<string> DispatchSites { get; } = new Sites(ledger);

    public IEntryLookup<string, DispatchTargetList> DispatchTargets { get; } = new Targets(ledger);

    public IEntryLookup<object, IReadOnlyCollection<DispatchTargetModel>> CurrentTargets { get; } = new SlotTargets(ledger);

    public IEnumerable<MethodInstanceModel> Boundaries
    {
        get
        {
            var bodies = _bodies.Current;
            foreach (var key in _rootKeys)
            {
                if (_ledger.Methods.TryGetValue(key, out var body)) yield return body.Method;
            }
            foreach (var method in _ledger.NativeCallbacks.Values.Concat(_ledger.MethodDescriptors.Values)
                .Concat(_ledger.Finalizers.Values).Concat(_ledger.SharedObjectDispatch.Values)
                .Concat(_ledger.JavaScriptConversions.Values).Concat(_ledger.StructMarshallingConstructors.Values))
            {
                yield return method;
            }
            foreach (var method in ReceiverFlowBoundaries.Generated(bodies, _ledger.Methods, _ledger.GeneratedCallees))
                yield return method;
        }
    }

    public IEnumerable<MethodInstanceModel> Entries => ReceiverFlowBoundaries.Entries(_bodies.Current, _ledger.Methods,
        _ledger.StaticInitializers.Concat(_ledger.ModuleInitializers), _ledger.ConstructedStaticInitializers);

    public IEnumerable<MethodInstanceModel> Callables => _ledger.CallableMethods.Values;


    // The ledger's bodies by canonical name, taking in those the closure applied since the last look.
    private sealed class LedgerBodies(ReachabilityLedger ledger) : IEntryLookup<string, ManagedMethodBody>
    {
        private readonly Dictionary<string, ManagedMethodBody> _bodies = new(StringComparer.Ordinal);
        private int _definitionsSeen = -1;
        private int _constructedSeen = -1;

        internal IReadOnlyDictionary<string, ManagedMethodBody> Current
        {
            get
            {
                if (ledger.Methods.Count != _definitionsSeen || ledger.ConstructedMethods.Count != _constructedSeen)
                {
                    foreach (var body in ledger.Methods.Values.Concat(ledger.ConstructedMethods.Values))
                        _bodies.TryAdd(body.Method.CanonicalName, body);
                    (_definitionsSeen, _constructedSeen) = (ledger.Methods.Count, ledger.ConstructedMethods.Count);
                }
                return _bodies;
            }
        }

        public bool TryGetValue(string method, [MaybeNullWhen(false)] out ManagedMethodBody body) =>
            Current.TryGetValue(method, out body);
    }

    private sealed class Sites(ReachabilityLedger ledger) : IMembership<string>
    {
        public bool Contains(string key) => ledger.DispatchDeclarations.ContainsKey(key);
    }

    // A call's list is its slot use: the ledger's targets for the slot grow as the closure allocates receivers.
    private sealed class Targets(ReachabilityLedger ledger) : IEntryLookup<string, DispatchTargetList>
    {
        public bool TryGetValue(string key, out DispatchTargetList value)
        {
            if (!ledger.DispatchDeclarations.TryGetValue(key, out var declaration))
            {
                value = default;
                return false;
            }
            var slot = new DispatchSlot(declaration.Operation, declaration.Declaration.CanonicalName);
            value = new(slot, ledger.DispatchTargets.TryGetValue(slot, out var targets) ? targets.Values : []);
            return true;
        }
    }

    private sealed class SlotTargets(ReachabilityLedger ledger) : IEntryLookup<object, IReadOnlyCollection<DispatchTargetModel>>
    {
        public bool TryGetValue(object list, [MaybeNullWhen(false)] out IReadOnlyCollection<DispatchTargetModel> targets)
        {
            if (list is DispatchSlot slot && ledger.DispatchTargets.TryGetValue(slot, out var found))
            {
                targets = found.Values;
                return true;
            }
            targets = null;
            return false;
        }
    }
}
