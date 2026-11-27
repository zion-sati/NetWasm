// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Analysis.ReceiverFlow;

// A node's value as the graph holds it: the objects that reach it, and the bounds of what it cannot follow.
internal sealed record FlowValue(ImmutableArray<FlowObject> Objects, ImmutableArray<FlowBound> Tops)
{
    public bool IsPrecise => Tops.IsEmpty;
}

// The receiver flow graph (RECEIVER_TYPE_FLOW_DESIGN.md, 4.5): nodes hold sets of objects and top bounds, edges carry
// them through casts, and only what a node newly gains travels on. Sets only grow and are capped, so propagation
// ends; each value crosses each edge once.
internal sealed class ReceiverFlowGraph(ITypeRelationshipClassifier relationships)
{
    // More objects than this make a node top, bounded by its declared type.
    internal const int MaximumObjects = 64;

    // More bounds than this make a node top without a bound.
    internal const int MaximumBounds = 8;

    private readonly ITypeRelationshipClassifier _relationships =
        relationships ?? throw new ArgumentNullException(nameof(relationships));
    private readonly Dictionary<FlowNodeKey, int> _ids = [];
    private readonly List<Node> _nodes = [];
    private readonly Dictionary<FlowObject, int> _objectIds = [];
    private readonly List<FlowObject> _objects = [];
    // Edges without casts by their two ends; the few with casts by their ends and casts.
    private readonly HashSet<long> _plainEdges = [];
    private readonly HashSet<(int From, int To, string Casts)> _castEdges = [];
    private readonly Queue<int> _pending = new();
    private readonly Dictionary<(CliTypeIdentity Type, CliTypeIdentity Target), bool> _passes = [];

    public int NodeCount => _nodes.Count;

    public int EdgeCount => _plainEdges.Count + _castEdges.Count;

    // Most nodes hold few values and have few edges: each list is made when first needed, and a node's objects are
    // searched in order until there are enough of them for a set.
    private sealed class Node(FlowNodeKey key)
    {
        private const int SetThreshold = 16;
        private HashSet<int>? _set;

        public FlowNodeKey Key { get; } = key;
        public CliTypeIdentity? Declared { get; set; }
        public List<int>? Objects { get; private set; }
        public List<FlowBound>? Tops { get; set; }
        public List<(int To, ImmutableArray<CliTypeIdentity> Casts)>? Out { get; set; }
        public List<Action<FlowObject?, FlowBound?>>? Watchers { get; set; }
        public int DeliveredObjects { get; set; }
        public int DeliveredTops { get; set; }
        public bool Queued { get; set; }
        public bool Capped { get; set; }
        public int ObjectCount => Objects?.Count ?? 0;
        public int TopCount => Tops?.Count ?? 0;
        public int OutCount => Out?.Count ?? 0;
        public int WatcherCount => Watchers?.Count ?? 0;

        internal bool AddObject(int id)
        {
            Objects ??= new List<int>(4);
            if (_set is not null ? !_set.Add(id) : Objects.Contains(id)) return false;
            Objects.Add(id);
            if (_set is null && Objects.Count == SetThreshold) _set = [.. Objects];
            return true;
        }
    }

    internal int Id(FlowNodeKey key, CliTypeIdentity? declared = null)
    {
        if (!_ids.TryGetValue(key, out var id))
        {
            id = _nodes.Count;
            _ids.Add(key, id);
            _nodes.Add(new Node(key));
        }
        var node = _nodes[id];
        if (node.Declared is null && declared is not null && !declared.ContainsGenericParameters) node.Declared = declared;
        return id;
    }

    internal bool Contains(FlowNodeKey key) => _ids.ContainsKey(key);

    internal void Apply(FlowEdge edge)
    {
        ArgumentNullException.ThrowIfNull(edge);
        var target = Id(edge.Target, edge.TargetType);
        switch (edge.Source)
        {
            case FlowSource.Object { Value: var value }:
                AddObject(target, value);
                break;
            case FlowSource.Top { Bound: var bound }:
                AddTop(target, bound);
                break;
            case FlowSource.Node { Key: var key, Casts: var casts }:
                AddEdge(Id(key), target, casts);
                break;
        }
    }

    internal void AddEdge(int from, int to, ImmutableArray<CliTypeIdentity> casts)
    {
        casts = casts.IsDefault ? [] : casts;
        if (from == to && casts.IsEmpty) return;
        var added = casts.IsEmpty
            ? _plainEdges.Add(((long)from << 32) | (uint)to)
            : _castEdges.Add((from, to, string.Join('|', casts.Select(static cast => cast.CanonicalName))));
        if (!added) return;
        var source = _nodes[from];
        (source.Out ??= []).Add((to, casts));
        // What the source already holds crosses the new edge now; later gains cross with the rest.
        for (var index = 0; index < source.DeliveredObjects; index++) Send(source.Objects![index], to, casts);
        for (var index = 0; index < source.DeliveredTops; index++) SendTop(source.Tops![index], to, casts);
    }

    internal void AddObject(int node, FlowObject value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (!_objectIds.TryGetValue(value, out var id))
        {
            id = _objects.Count;
            _objectIds.Add(value, id);
            _objects.Add(value);
        }
        AddObjectId(node, id);
    }

    internal void AddTop(int node, FlowBound bound)
    {
        var target = _nodes[node];
        var tops = target.Tops ??= [];
        foreach (var existing in tops)
        {
            if (existing.Types.IsEmpty || SameTypes(existing, bound)) return;
        }
        if (tops.Count >= MaximumBounds) bound = new FlowBound([], "more than eight bounds");
        tops.Add(bound);
        Enqueue(target, node);
    }

    // Calls back with every object and bound the node holds and gains, during propagation.
    internal void Watch(int node, Action<FlowObject?, FlowBound?> watcher)
    {
        ArgumentNullException.ThrowIfNull(watcher);
        var target = _nodes[node];
        (target.Watchers ??= []).Add(watcher);
        for (var index = 0; index < target.DeliveredObjects; index++) watcher(_objects[target.Objects![index]], null);
        for (var index = 0; index < target.DeliveredTops; index++) watcher(null, target.Tops![index]);
    }

    internal void Propagate()
    {
        while (_pending.TryDequeue(out var id))
        {
            var node = _nodes[id];
            node.Queued = false;
            while (node.DeliveredObjects < node.ObjectCount || node.DeliveredTops < node.TopCount)
            {
                // Edges and watchers added while a value is delivered have it already (AddEdge, Watch); going on to
                // them again only finds it there.
                if (node.DeliveredObjects < node.ObjectCount)
                {
                    var value = node.Objects![node.DeliveredObjects++];
                    for (var index = 0; index < node.OutCount; index++) Send(value, node.Out![index].To, node.Out[index].Casts);
                    for (var index = 0; index < node.WatcherCount; index++) node.Watchers![index](_objects[value], null);
                }
                else
                {
                    var bound = node.Tops![node.DeliveredTops++];
                    for (var index = 0; index < node.OutCount; index++) SendTop(bound, node.Out![index].To, node.Out[index].Casts);
                    for (var index = 0; index < node.WatcherCount; index++) node.Watchers![index](null, bound);
                }
            }
        }
    }

    public bool HasPending => _pending.Count > 0;

    internal FlowValue Value(FlowNodeKey key)
    {
        if (!_ids.TryGetValue(key, out var id)) return new FlowValue([], []);
        var node = _nodes[id];
        return new FlowValue([.. (node.Objects ?? []).Select(index => _objects[index])], [.. node.Tops ?? []]);
    }

    // Whether an object of the type can pass a cast to the target: it can unless they are unrelated.
    internal bool Passes(CliTypeIdentity type, CliTypeIdentity target)
    {
        if (type.Equals(target)) return true;
        var key = (type, target);
        if (!_passes.TryGetValue(key, out var passes))
        {
            passes = MayRelateArguments(type, target) &&
                _relationships.Classify(type, target).Characteristics is not TypeRelationshipCharacteristics.None;
            _passes.Add(key, passes);
        }
        return passes;
    }

    // Two instantiations of one generic definition are related only where each pair of arguments is the same type or
    // two reference types related one way or the other (variance): a cheap test that answers most of the questions an
    // invoke's or a cast's family of instantiations asks, before the classifier.
    private bool MayRelateArguments(CliTypeIdentity type, CliTypeIdentity target)
    {
        if (type is not { Shape: CliTypeShape.GenericInstantiation, ElementType: { } definition } ||
            target is not { Shape: CliTypeShape.GenericInstantiation } || !definition.Equals(target.ElementType) ||
            type.TypeArguments.Length != target.TypeArguments.Length)
        {
            return true;
        }
        for (var index = 0; index < type.TypeArguments.Length; index++)
        {
            var (argument, other) = (type.TypeArguments[index], target.TypeArguments[index]);
            if (argument.Equals(other)) continue;
            if (argument.IsValueType || other.IsValueType) return false;
            if (!Passes(argument, other) && !Passes(other, argument)) return false;
        }
        return true;
    }

    // Whether a type is within a bound: assignable to every type of its conjunction.
    internal bool Within(CliTypeIdentity type, FlowBound bound)
    {
        foreach (var target in bound.Types)
        {
            if (!Passes(type, target)) return false;
        }
        return true;
    }

    private void Send(int value, int to, ImmutableArray<CliTypeIdentity> casts)
    {
        var type = _objects[value].Type;
        foreach (var cast in casts)
        {
            if (!Passes(type, cast)) return;
        }
        AddObjectId(to, value);
    }

    private void AddObjectId(int node, int id)
    {
        var target = _nodes[node];
        if (target.Capped || !target.AddObject(id)) return;
        if (target.ObjectCount > MaximumObjects)
        {
            target.Capped = true;
            AddTop(node, FlowBound.Of(target.Declared, $"more than {MaximumObjects} types"));
        }
        Enqueue(target, node);
    }

    private void SendTop(FlowBound bound, int to, ImmutableArray<CliTypeIdentity> casts)
    {
        foreach (var cast in casts) bound = bound.And(cast);
        AddTop(to, bound);
    }

    private void Enqueue(Node node, int id)
    {
        if (node.Queued) return;
        node.Queued = true;
        _pending.Enqueue(id);
    }

    private static bool SameTypes(FlowBound left, FlowBound right)
    {
        if (left.Types.Length != right.Types.Length) return false;
        foreach (var type in left.Types)
        {
            if (!right.Types.Contains(type)) return false;
        }
        return true;
    }
}
