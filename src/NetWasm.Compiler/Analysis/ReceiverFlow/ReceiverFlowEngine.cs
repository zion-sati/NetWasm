// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.ControlFlow;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Analysis.ReceiverFlow;

// The services the receiver flow engine and its summarizer use.
internal sealed class ReceiverFlowServices(
    ICalledMethodResolver calls,
    ITypeOperandResolver typeOperands,
    IFieldRepository fields,
    IDelegateTypeRecognizer delegates,
    IDispatchSiteKeyBuilder siteKeys,
    ITypeRelationshipClassifier relationships,
    IRuntimeIntrinsicRegistry intrinsics,
    IRuntimeWrittenFields runtimeFields,
    IReflectionObjectBounds reflectionObjects)
{
    public ICalledMethodResolver Calls { get; } = calls;
    public ITypeOperandResolver TypeOperands { get; } = typeOperands;
    public IFieldRepository Fields { get; } = fields;
    public IDelegateTypeRecognizer Delegates { get; } = delegates;
    public IDispatchSiteKeyBuilder SiteKeys { get; } = siteKeys;
    public ITypeRelationshipClassifier Relationships { get; } = relationships;
    public IRuntimeIntrinsicRegistry Intrinsics { get; } = intrinsics;
    public IRuntimeWrittenFields RuntimeFields { get; } = runtimeFields;
    public IReflectionObjectBounds ReflectionObjects { get; } = reflectionObjects;
}

// The receiver flow analysis over a program view (RECEIVER_TYPE_FLOW_DESIGN.md, 4): methods join as flow reaches them,
// from the view's boundaries and entries through direct calls, delegates and the targets each dispatched call's
// receivers resolve to. A settle runs to the fixpoint of what the view holds; a closure's view grows, and the next
// settle goes on from there with the same graph.
internal sealed class ReceiverFlowEngine
{
    internal const int MaximumListedSites = 200;
    internal const int MaximumListedWarnings = 50;
    internal const int MaximumListedReceivers = 16;

    // A dispatched call's targets by receiver type, shared by the calls whose target lists are one list (the program
    // builder selects one list per slot use and bound; a closure has one per slot use), and grown as the list grows.
    private sealed class TargetIndex(int number)
    {
        public Dictionary<CliTypeIdentity, List<DispatchTargetModel>> ByReceiver { get; } = [];

        public HashSet<string> Methods { get; } = new(StringComparer.Ordinal);

        private readonly HashSet<(CliTypeIdentity, string)> _known = [];

        public HashSet<object> Lists { get; } = [];

        public List<SiteState> Sites { get; } = [];

        // The hub nodes' owner, and the targets connected to them.
        public string Hub { get; } = $"hub#{number}";

        public HashSet<string> Connected { get; } = new(StringComparer.Ordinal);

        // A list with this many receiver types shares its calls' arguments and results through hub nodes, as the
        // emitter shares their dispatch (SharedDispatchPolicy): each target connects once, not once per call.
        public bool Shared => ByReceiver.Count >= SharedDispatchPolicy.MinimumTargets;

        public bool HubsMade { get; set; }

        // How many targets each list had when last read: a list read again with no more is skipped.
        public Dictionary<object, int> Seen { get; } = [];

        // The targets the index did not have, added.
        internal List<DispatchTargetModel> Add(IReadOnlyCollection<DispatchTargetModel> targets)
        {
            var added = new List<DispatchTargetModel>();
            foreach (var target in targets)
            {
                if (!_known.Add((target.ReceiverType, target.Method.CanonicalName))) continue;
                if (!ByReceiver.TryGetValue(target.ReceiverType, out var list)) ByReceiver.Add(target.ReceiverType, list = []);
                list.Add(target);
                Methods.Add(target.Method.CanonicalName);
                added.Add(target);
            }
            return added;
        }
    }

    private sealed class SiteState(FlowSite site, string caller, TargetIndex? index)
    {
        public FlowSite Site { get; } = site;
        public string Caller { get; } = caller;
        public TargetIndex? Index { get; } = index;
        public HashSet<string> Targets { get; } = [];
        // The receiver types dispatched for, those that arrived (with a target or not yet), and the bounds of what
        // arrived unfollowed.
        public HashSet<CliTypeIdentity> Dispatched { get; } = [];
        public HashSet<CliTypeIdentity> Arrived { get; } = [];
        public List<FlowBound> Bounds { get; } = [];
        public HashSet<string> SharesHubs { get; } = new(StringComparer.Ordinal);
        public HashSet<string> TopBounds { get; } = [];
        public string? TopCause { get; set; }
        public bool IsGenericVirtual => Site.Kind == FlowSiteKind.Dispatch && !Site.Declaration.MethodArguments.IsDefaultOrEmpty;
    }

        private readonly ReceiverFlowServices _services;
        private readonly IReceiverFlowProgramView _view;
        private readonly ReceiverFlowSummarizer _summarizer;
        private readonly HashSet<string> _reached = new(StringComparer.Ordinal);
        // Methods the flow reached before the closure had their bodies (a member descriptor's method, a call the closure
        // has yet to analyze): applied as methods without a body then, and with their bodies once those arrive.
        private readonly Dictionary<string, MethodInstanceModel> _awaitingBodies = new(StringComparer.Ordinal);
        private readonly Queue<MethodInstanceModel> _queue = new();
        private readonly HashSet<string> _boundaries = new(StringComparer.Ordinal);
        private readonly HashSet<string> _delegateTargets = new(StringComparer.Ordinal);
        private readonly HashSet<FlowObject.Delegate> _delegateSet = [];
        private readonly List<FlowObject.Delegate> _delegateObjects = [];
        // Delegate targets by delegate type: compatibility is a question of types, asked once per pair.
        private readonly Dictionary<CliTypeIdentity, List<MethodInstanceModel>> _delegateTargetsByType = [];
        // Delegate types by family (generic definition, or the type itself): a delegate is assignable only to a
        // type of its own family, its own or a variant instantiation of it.
        private readonly Dictionary<string, List<CliTypeIdentity>> _delegateTypesByFamily = new(StringComparer.Ordinal);
        private readonly Dictionary<string, List<TopInvokeGroup>> _topInvokesByFamily = new(StringComparer.Ordinal);
        // Invokes whose receivers the analysis cannot follow, by bound: each new delegate is tested once per bound.
        private readonly Dictionary<string, TopInvokeGroup> _topInvokes = new(StringComparer.Ordinal);
        private readonly Dictionary<string, FlowBound> _escapedBounds = new(StringComparer.Ordinal);
        private readonly Dictionary<string, SiteState> _sites = new(StringComparer.Ordinal);
        private readonly HashSet<(string Site, string Target)> _connected = [];
        private readonly Dictionary<object, TargetIndex> _indexes = [];
        private readonly List<(CliTypeIdentity Type, FlowNodeKey Source)> _reinterpretations = [];

        public ReceiverFlowEngine(ReceiverFlowServices services, IReceiverFlowProgramView view)
        {
            _services = services ?? throw new ArgumentNullException(nameof(services));
            _view = view ?? throw new ArgumentNullException(nameof(view));
            Graph = new ReceiverFlowGraph(services.Relationships);
            _summarizer = new ReceiverFlowSummarizer(services.Calls, services.TypeOperands, services.Fields,
                services.Delegates, services.SiteKeys, view.DispatchSites.Contains, new ReachingLocalStores(),
                services.RuntimeFields, services.ReflectionObjects);
            var escape = Graph.Id(FlowNodeKey.Escape);
            Graph.Watch(escape, Escaped);
        }

        public ReceiverFlowGraph Graph { get; }

        public int MethodCount => _view.Bodies.Count();

        public int ReachedCount => _reached.Count(name => _view.MethodBodies.TryGetValue(name, out _));

        // The view's boundaries and entries, again at each settle: a closure's ledger has more each time.
        internal void ReachRoots()
        {
            foreach (var method in _view.Boundaries) Boundary(method);
            foreach (var method in _view.Callables) Reach(method);
            foreach (var method in _view.Entries) Reach(method);
        }

        // The methods the program compiles that the flow has not reached, each made a boundary (unknown parameters
        // within their types), by name in ordinal order: the closure reached them by a path the flow does not model, so
        // a call in them would otherwise wait for receivers that never come.
        internal ImmutableArray<string> ReachUnreachedBodies()
        {
            var made = new List<string>();
            foreach (var body in _view.Bodies.ToArray())
            {
                if (_reached.Contains(body.Method.CanonicalName)) continue;
                Boundary(body.Method);
                made.Add(body.Method.CanonicalName);
            }
            made.Sort(StringComparer.Ordinal);
            return [.. made];
        }

        internal void ReachEverything()
        {
            foreach (var body in _view.Bodies.ToArray())
            {
                if (!_reached.Contains(body.Method.CanonicalName)) Boundary(body.Method);
            }
        }

        internal void Settle()
        {
            RefreshTargets();
            ApplyArrivedBodies();
            while (true)
            {
                while (_queue.TryDequeue(out var method)) Apply(method);
                Graph.Propagate();
                if (_queue.Count > 0 || Graph.HasPending) continue;
                // A method whose address the program takes other than for a delegate the analysis follows is called
                // with anything.
                var added = false;
                foreach (var method in _view.Callables)
                {
                    if (!_delegateTargets.Contains(method.CanonicalName) && !_boundaries.Contains(method.CanonicalName))
                    {
                        Boundary(method);
                        added = true;
                    }
                }
                if (!added && _queue.Count == 0 && !Graph.HasPending) return;
            }
        }

        public IEnumerable<string> Reached => _reached;

        internal ImmutableArray<string> UnreachedSample(int count) =>
            [.. _view.Bodies.Select(static body => body.Method.CanonicalName).Where(name => !_reached.Contains(name))
                .Order(StringComparer.Ordinal).Take(count)];

        internal ReceiverFlowPrecision Precision()
        {
            var genericVirtual = new Totals();
            var virtualCalls = new Totals();
            var invokes = new Totals();
            var targetsToday = new HashSet<string>(StringComparer.Ordinal);
            var targetsByFlow = new HashSet<string>(StringComparer.Ordinal);
            var causes = new Dictionary<string, int>(StringComparer.Ordinal);
            var compatibleByType = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var state in _sites.Values)
            {
                if (state.Site.Kind == FlowSiteKind.DelegateInvoke)
                {
                    var invoked = state.Site.Declaration.DeclaringType;
                    if (!compatibleByType.TryGetValue(invoked.CanonicalName, out var compatible))
                    {
                        compatible = _delegateObjects.Count(item => Graph.Passes(item.DelegateType, invoked));
                        compatibleByType.Add(invoked.CanonicalName, compatible);
                    }
                    invokes.Add(state.TopCause is null, compatible, state.Targets.Count, compatible, state.Targets.Count);
                    continue;
                }
                var index = state.Index!;
                var totals = state.IsGenericVirtual ? genericVirtual : virtualCalls;
                totals.Add(state.TopCause is null, index.ByReceiver.Count, state.Dispatched.Count, index.Methods.Count,
                    state.Targets.Count);
                if (!state.IsGenericVirtual) continue;
                targetsToday.UnionWith(index.Methods);
                targetsByFlow.UnionWith(state.Targets);
                if (state.TopCause is { } cause) causes[cause] = causes.GetValueOrDefault(cause) + 1;
            }
            return new ReceiverFlowPrecision(genericVirtual.Build(), virtualCalls.Build(), invokes.Build(),
                targetsToday.Count, targetsByFlow.Count,
                [.. causes.OrderByDescending(static pair => pair.Value).ThenBy(static pair => pair.Key, StringComparer.Ordinal)
                    .Select(static pair => new ReceiverFlowCause(pair.Key, pair.Value))]);
        }

        // For each dispatched call, the receiver types that reached it and the bounds of what reached it unfollowed.
        internal IEnumerable<(string Site, MethodInstanceModel Declaration, IReadOnlySet<CliTypeIdentity> Arrived,
            IReadOnlyList<FlowBound> Bounds)> Admissions() =>
            _sites.Values.Where(static state => state.Site.Kind == FlowSiteKind.Dispatch)
                .Select(static state => (state.Site.Key, state.Site.Declaration, (IReadOnlySet<CliTypeIdentity>)state.Arrived,
                    (IReadOnlyList<FlowBound>)state.Bounds));

        internal ImmutableDictionary<string, ImmutableArray<CliTypeIdentity>> PreciseReceivers() =>
            _sites.Values.Where(static state => state.Site.Kind == FlowSiteKind.Dispatch && state.TopCause is null)
                .ToImmutableDictionary(static state => state.Site.Key,
                    static state => state.Dispatched.OrderBy(static type => type.CanonicalName, StringComparer.Ordinal)
                        .ToImmutableArray(), StringComparer.Ordinal);

        internal ImmutableArray<ReceiverFlowSite> ListedSites() =>
        [
            .. _sites.Values.Where(static state => state.IsGenericVirtual)
                .Select(static state => new ReceiverFlowSite(state.Site.Key, state.Site.Declaration.CanonicalName,
                    state.Index!.Methods.Count, state.Targets.Count, state.TopCause is null, state.TopCause,
                    [.. state.Dispatched.Select(static type => type.CanonicalName).Order(StringComparer.Ordinal)
                        .Take(MaximumListedReceivers)]))
                .OrderByDescending(static site => site.TargetsToday - site.TargetsByFlow)
                .ThenBy(static site => site.Site, StringComparer.Ordinal)
                .Take(MaximumListedSites),
        ];

        // Unsafe.As<T>(object) on an object that is not a T: a field access through T reads another type's fields,
        // which the analysis does not follow yet (RECEIVER_TYPE_FLOW_DESIGN.md, 4.3).
        internal ImmutableArray<string> Warnings()
        {
            var warnings = new SortedSet<string>(StringComparer.Ordinal);
            foreach (var (type, source) in _reinterpretations)
            {
                var value = Graph.Value(source);
                foreach (var item in value.Objects)
                {
                    if (!Graph.Passes(item.Type, type)) warnings.Add($"Unsafe.As<{type.CanonicalName}> on {item.Type.CanonicalName}");
                }
                foreach (var bound in value.Tops)
                {
                    if (!bound.Types.Any(target => Graph.Passes(target, type)))
                        warnings.Add($"Unsafe.As<{type.CanonicalName}> on top ({bound.Cause})");
                }
            }
            return [.. warnings.Take(MaximumListedWarnings)];
        }

        private void Reach(MethodInstanceModel method)
        {
            if (_reached.Add(method.CanonicalName)) _queue.Enqueue(method);
        }

        // A method code the analysis does not see may call: its parameters hold anything of their declared types.
        private void Boundary(MethodInstanceModel method)
        {
            Reach(method);
            if (!_boundaries.Add(method.CanonicalName)) return;
            var count = ArgumentCount(method);
            for (var argument = 0; argument < count; argument++)
            {
                if (ArgumentType(method, argument) is { } type && IsReference(type))
                    Graph.AddTop(Parameter(method, argument, type), FlowBound.Of(type, "boundary method's parameter"));
            }
        }

        private void Apply(MethodInstanceModel method)
        {
            if (!_view.MethodBodies.TryGetValue(method.CanonicalName, out var body))
            {
                Bodiless(method);
                if (method.Definition.HasManagedBody) _awaitingBodies.TryAdd(method.CanonicalName, method);
                return;
            }
            // An intrinsic's lowered calls do not run its IL, so its result and what it does with its arguments are
            // unknown, as for a method without a body. The closure still compiles its body where it reached it (a call
            // the emitter does not lower runs it), so the body is applied too, with unknown parameters, and its own
            // calls are followed.
            if (_services.Intrinsics.TryGetIntrinsic(method.Definition.Key, out _))
            {
                Bodiless(method);
                Boundary(method);
            }
            var summary = _summarizer.Summarize(body);
            foreach (var edge in summary.Edges)
            {
                if (edge.Source is FlowSource.Object { Value: FlowObject.Delegate created }) Created(created);
                Graph.Apply(edge);
            }
            foreach (var site in summary.Sites) Register(site, method.CanonicalName);
            foreach (var reached in summary.Reached) Reach(reached);
            foreach (var escaped in summary.AddressEscapes) Boundary(escaped);
            _reinterpretations.AddRange(summary.Reinterpretations);
            if (method.Definition.IsRuntimeAsync && IsReference(method.Signature.ReturnSignatureType))
            {
                var task = method.Signature.ReturnSignatureType;
                Graph.AddTop(Graph.Id(new FlowNodeKey(FlowNodeKind.Return, method.CanonicalName), task),
                    FlowBound.Of(task, "runtime-async method's task"));
            }
        }

        // A method without an IL body returns what the analysis cannot follow; a delegate it is given, or anything a
        // native import is given, may be called where the analysis does not see.
        private void Bodiless(MethodInstanceModel method)
        {
            var returned = method.Signature.ReturnSignatureType;
            if (IsReference(returned))
            {
                Graph.AddTop(Graph.Id(new FlowNodeKey(FlowNodeKind.Return, method.CanonicalName), returned),
                    FlowBound.Of(returned, "method without an IL body"));
            }
            var definition = method.Definition;
            var native = definition.NativeImport is not null || definition.UnboundNativeImport is not null ||
                definition.JSImport is not null || definition.WitImport is not null ||
                _services.Intrinsics.TryGetIntrinsic(definition.Key, out _);
            var escape = Graph.Id(FlowNodeKey.Escape);
            var count = ArgumentCount(method);
            for (var argument = 0; argument < count; argument++)
            {
                if (ArgumentType(method, argument) is { } type && IsReference(type) && (native || IsDelegateLike(type)))
                    Graph.AddEdge(Parameter(method, argument, type), escape, []);
            }
        }

        private void Register(FlowSite site, string caller)
        {
            if (_sites.ContainsKey(site.Key)) return;
            TargetIndex? index = null;
            if (site.Kind == FlowSiteKind.Dispatch)
            {
                if (!_view.DispatchTargets.TryGetValue(site.Key, out var found)) return;
                var (list, targets) = found;
                if (!_indexes.TryGetValue(list, out index))
                {
                    index = new TargetIndex(_indexes.Count);
                    _indexes.Add(list, index);
                }
                // A list the index has read is read again only when it grew, which happens between settles, and each
                // settle refreshes the grown lists before it applies a body; many calls share one list.
                if (!index.Seen.TryGetValue(list, out var seen) || seen != targets.Count)
                {
                    index.Add(targets);
                    index.Lists.Add(list);
                    index.Seen[list] = targets.Count;
                }
            }
            var state = new SiteState(site, caller, index);
            _sites.Add(site.Key, state);
            var receiver = Graph.Id(new FlowNodeKey(FlowNodeKind.Receiver, site.Key), site.Declaration.DeclaringType);
            if (site.Kind == FlowSiteKind.Dispatch)
            {
                index!.Sites.Add(state);
                if (index.Shared) Share(index, state);
                Graph.Watch(receiver, (item, bound) =>
                {
                    if (item is not null)
                    {
                        state.Arrived.Add(item.Type);
                        if (index.ByReceiver.TryGetValue(item.Type, out var targets)) Dispatch(state, item.Type, targets);
                    }
                    if (bound is null) return;
                    state.TopCause ??= bound.Cause;
                    state.Bounds.Add(bound);
                    foreach (var (type, targetsOfType) in index.ByReceiver)
                    {
                        if (!state.Dispatched.Contains(type) && Graph.Within(type, bound)) Dispatch(state, type, targetsOfType);
                    }
                });
                return;
            }
            Graph.Watch(receiver, (item, bound) =>
            {
                if (item is FlowObject.Delegate known) Invoke(state, known.Target);
                else if (item is not null) TopInvoke(state, FlowBound.Of(item.Type, "delegate the analysis did not see made"));
                if (bound is not null) TopInvoke(state, bound);
            });
        }

        // The bodies the closure analyzed since the last settle of methods the flow applied without one: each is applied
        // now, in name order, so its calls, allocations and returns join the flow.
        private void ApplyArrivedBodies()
        {
            if (_awaitingBodies.Count == 0) return;
            List<string>? arrived = null;
            foreach (var name in _awaitingBodies.Keys)
            {
                if (_view.MethodBodies.TryGetValue(name, out _)) (arrived ??= []).Add(name);
            }
            if (arrived is null) return;
            arrived.Sort(StringComparer.Ordinal);
            foreach (var name in arrived)
            {
                _queue.Enqueue(_awaitingBodies[name]);
                _awaitingBodies.Remove(name);
            }
        }

        // A list grown since the last settle (a closure resolves more targets as it goes): each new target serves the
        // calls whose receivers already include its type, and the calls that are top within its type.
        private void RefreshTargets()
        {
            foreach (var index in _indexes.Values)
            {
                var added = new List<DispatchTargetModel>();
                foreach (var list in index.Lists)
                {
                    if (!_view.CurrentTargets.TryGetValue(list, out var targets) || targets.Count == index.Seen.GetValueOrDefault(list))
                        continue;
                    index.Seen[list] = targets.Count;
                    added.AddRange(index.Add(targets));
                }
                if (added.Count == 0) continue;
                if (index.Shared && !index.HubsMade)
                {
                    foreach (var state in index.Sites) Share(index, state);
                }
                // Only the new targets: each serves the calls whose receivers include its type or whose bounds admit it.
                foreach (var target in added)
                {
                    DispatchTargetModel[] one = [target];
                    foreach (var state in index.Sites)
                    {
                        var type = target.ReceiverType;
                        if (state.Arrived.Contains(type) || state.Dispatched.Contains(type) ||
                            state.Bounds.Any(bound => Graph.Within(type, bound)))
                        {
                            Dispatch(state, type, one, again: true);
                        }
                    }
                }
            }
        }

        // The call's arguments and result go through its list's hubs.
        private void Share(TargetIndex index, SiteState state)
        {
            index.HubsMade = true;
            if (!index.Shared || !state.SharesHubs.Add(index.Hub)) return;
            for (var argument = 0; argument < state.Site.ArgumentCount; argument++)
            {
                Graph.AddEdge(Graph.Id(new FlowNodeKey(FlowNodeKind.Argument, state.Site.Key, argument)),
                    Graph.Id(new FlowNodeKey(FlowNodeKind.Argument, index.Hub, argument)), []);
            }
            if (state.Site.Result is { } result)
                Graph.AddEdge(Graph.Id(new FlowNodeKey(FlowNodeKind.Result, index.Hub)), Graph.Id(result), []);
        }

        // A dispatched call reaches a target for a receiver: the target's this is that receiver, its parameters take
        // the call's arguments, and its returns are the call's result.
        private void Dispatch(SiteState state, CliTypeIdentity receiver, IReadOnlyCollection<DispatchTargetModel> targets,
            bool again = false)
        {
            if (!state.Dispatched.Add(receiver) && !again) return;
            foreach (var target in targets)
            {
                var method = target.Method;
                state.Targets.Add(method.CanonicalName);
                if (!method.Definition.IsStatic && !method.DeclaringType.IsValueType)
                    Graph.AddObject(Parameter(method, 0, method.DeclaringType), new FlowObject.Exact(receiver));
                if (state.Index is { Shared: true } index && state.SharesHubs.Contains(index.Hub))
                {
                    if (index.Connected.Add(method.CanonicalName)) ConnectHub(index, state.Site, method);
                }
                else if (_connected.Add((state.Site.Key, method.CanonicalName)))
                {
                    Connect(state, method, method.Definition.IsStatic ? 0 : 1);
                }
            }
        }

        private void Invoke(SiteState state, MethodInstanceModel target)
        {
            state.Targets.Add(target.CanonicalName);
            if (!_connected.Add((state.Site.Key, target.CanonicalName))) return;
            // An instance target or a closed static takes the delegate's bound object first.
            var shift = !target.Definition.IsStatic ||
                target.Signature.ParameterTypes.Length == state.Site.ArgumentCount + 1 ? 1 : 0;
            Connect(state, target, shift);
        }

        // A target of a shared list takes every call's arguments of that list and gives its returns to all of them.
        private void ConnectHub(TargetIndex index, FlowSite site, MethodInstanceModel target)
        {
            var shift = target.Definition.IsStatic ? 0 : 1;
            for (var argument = 0; argument < site.ArgumentCount; argument++)
            {
                if (ArgumentType(target, argument + shift) is { } type && IsReference(type))
                {
                    Graph.AddEdge(Graph.Id(new FlowNodeKey(FlowNodeKind.Argument, index.Hub, argument)),
                        Parameter(target, argument + shift, type), []);
                }
            }
            if (IsReference(target.Signature.ReturnSignatureType))
            {
                Graph.AddEdge(Graph.Id(new FlowNodeKey(FlowNodeKind.Return, target.CanonicalName),
                    target.Signature.ReturnSignatureType), Graph.Id(new FlowNodeKey(FlowNodeKind.Result, index.Hub)), []);
            }
            Reach(target);
        }

        private void Connect(SiteState state, MethodInstanceModel target, int shift)
        {
            for (var index = 0; index < state.Site.ArgumentCount; index++)
            {
                if (ArgumentType(target, index + shift) is { } type && IsReference(type))
                {
                    Graph.AddEdge(Graph.Id(new FlowNodeKey(FlowNodeKind.Argument, state.Site.Key, index)),
                        Parameter(target, index + shift, type), []);
                }
            }
            if (state.Site.Result is { } result)
            {
                Graph.AddEdge(Graph.Id(new FlowNodeKey(FlowNodeKind.Return, target.CanonicalName),
                    target.Signature.ReturnSignatureType), Graph.Id(result), []);
            }
            Reach(target);
        }

        // An invoke whose receiver the analysis cannot follow may call any delegate of a compatible type, and one
        // made where the analysis does not see, whose result it cannot follow.
        private void TopInvoke(SiteState state, FlowBound bound)
        {
            bound = bound.And(state.Site.Declaration.DeclaringType);
            state.TopCause ??= bound.Cause;
            var key = Key(bound);
            if (!state.TopBounds.Add(key)) return;
            if (!_topInvokes.TryGetValue(key, out var group))
            {
                var family = Family(state.Site.Declaration.DeclaringType);
                group = new TopInvokeGroup(bound);
                foreach (var type in _delegateTypesByFamily.GetValueOrDefault(family) ?? [])
                {
                    if (Graph.Within(type, bound)) group.Targets.AddRange(_delegateTargetsByType[type]);
                }
                _topInvokes.Add(key, group);
                if (!_topInvokesByFamily.TryGetValue(family, out var groups))
                {
                    groups = [];
                    _topInvokesByFamily.Add(family, groups);
                }
                groups.Add(group);
            }
            group.Sites.Add(state);
            for (var index = 0; index < group.Targets.Count; index++) Invoke(state, group.Targets[index]);
            if (state.Site.Result is { } result)
            {
                var returned = state.Site.Declaration.Signature.ReturnSignatureType;
                Graph.AddTop(Graph.Id(result, returned), FlowBound.Of(returned, "invoke of a delegate the analysis does not follow"));
            }
        }

        private void Created(FlowObject.Delegate created)
        {
            _delegateTargets.Add(created.Target.CanonicalName);
            if (!_delegateSet.Add(created)) return;
            _delegateObjects.Add(created);
            var family = Family(created.DelegateType);
            if (!_delegateTargetsByType.TryGetValue(created.DelegateType, out var ofType))
            {
                ofType = [];
                _delegateTargetsByType.Add(created.DelegateType, ofType);
                if (!_delegateTypesByFamily.TryGetValue(family, out var types))
                {
                    types = [];
                    _delegateTypesByFamily.Add(family, types);
                }
                types.Add(created.DelegateType);
            }
            ofType.Add(created.Target);
            foreach (var group in (_topInvokesByFamily.GetValueOrDefault(family) ?? []).ToArray())
            {
                if (!Graph.Within(created.DelegateType, group.Bound)) continue;
                group.Targets.Add(created.Target);
                for (var index = 0; index < group.Sites.Count; index++) Invoke(group.Sites[index], created.Target);
            }
            foreach (var bound in _escapedBounds.Values)
            {
                if (Graph.Within(created.DelegateType, bound)) Boundary(created.Target);
            }
        }

        private void Escaped(FlowObject? item, FlowBound? bound)
        {
            if (item is FlowObject.Delegate known) Boundary(known.Target);
            if (bound is null || !_escapedBounds.TryAdd(Key(bound), bound)) return;
            foreach (var (type, targets) in _delegateTargetsByType.ToArray())
            {
                if (!Graph.Within(type, bound)) continue;
                foreach (var target in targets.ToArray()) Boundary(target);
            }
        }

        private static string Family(CliTypeIdentity type) =>
            type is { Shape: CliTypeShape.GenericInstantiation, ElementType: { } definition }
                ? definition.CanonicalName
                : type.CanonicalName;

        private static string Key(FlowBound bound) => string.Join('|', bound.Types.Select(static type => type.CanonicalName));

        private sealed class TopInvokeGroup(FlowBound bound)
        {
            public FlowBound Bound { get; } = bound;
            public List<SiteState> Sites { get; } = [];
            public List<MethodInstanceModel> Targets { get; } = [];
        }

        private int Parameter(MethodInstanceModel method, int argument, CliTypeIdentity type) =>
            Graph.Id(new FlowNodeKey(FlowNodeKind.Parameter, method.CanonicalName, argument), type);

        private bool IsDelegateLike(CliTypeIdentity type) =>
            type.FullName is "System.Delegate" or "System.MulticastDelegate" || _services.Delegates.Recognize(type);

        private sealed class Totals
        {
            private int _sites;
            private int _precise;
            private long _receiversToday;
            private long _receiversByFlow;
            private long _targetsToday;
            private long _targetsByFlow;

            internal void Add(bool precise, long receiversToday, long receiversByFlow, long targetsToday, long targetsByFlow)
            {
                _sites++;
                if (precise) _precise++;
                _receiversToday += receiversToday;
                _receiversByFlow += receiversByFlow;
                _targetsToday += targetsToday;
                _targetsByFlow += targetsByFlow;
            }

            internal ReceiverFlowSiteTotals Build() =>
                new(_sites, _precise, _receiversToday, _receiversByFlow, _targetsToday, _targetsByFlow);
        }

    // A method's IL arguments: an instance method's this first.
    private static int ArgumentCount(MethodInstanceModel method) =>
        method.Signature.ParameterTypes.Length + (method.Definition.IsStatic ? 0 : 1);

    private static CliTypeIdentity? ArgumentType(MethodInstanceModel method, int argument)
    {
        if (!method.Definition.IsStatic)
        {
            if (argument == 0) return method.DeclaringType.IsValueType ? null : method.DeclaringType;
            argument--;
        }
        var parameters = method.Signature.ParameterSignatureTypes;
        return (uint)argument < (uint)parameters.Length ? parameters[argument] : null;
    }

    private static bool IsReference(CliTypeIdentity type) => type.StackKind == CliValueKind.ManagedReference;
}
