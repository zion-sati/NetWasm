// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Collections.Generic;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Metadata.RuntimeProvidedMembers.RuntimeAsync;

internal interface IRuntimeAsyncSuspensionFacts
{
    // Whether a call of a runtime-async method can return with it suspended. A direct await of one that cannot completes
    // at once, and one that cannot is never resumed.
    bool CanSuspend(MethodInstanceModel method);
}

// Every runtime-async method may suspend: the lowering of a compile whose analysis stays within each method (O0 and None,
// where the early cutoff serves unedited methods from the last compile, so no method's code may follow another's).
internal sealed class AnyRuntimeAsyncMethodMaySuspend : IRuntimeAsyncSuspensionFacts
{
    public bool CanSuspend(MethodInstanceModel method) => true;
}

// A runtime-async method can suspend at an await of something that may not be complete: an awaitable or an awaiter (a
// leaf), a virtual or interface call (whose override may be any lowered method, or not lowered at all), or a direct call
// of a runtime-async method that can. The facts are the least fixed point over the direct-call graph, each method read
// as its lowering reads it (RuntimeAsyncSourceReader, RuntimeAsyncAwaits): the components of the graph found from the
// method asked about (Tarjan's, iteratively), each component suspending when a member has a leaf or virtual await or
// a direct callee outside it suspends. A method whose source cannot be read is taken to suspend.
// Cost: each method's source read and classified once, and each direct call followed once, over the methods reached.
internal sealed class RuntimeAsyncSuspensionFacts(
    IRuntimeAsyncSourceReader sources,
    Func<IRuntimeAsyncMemberResolver> members) : IRuntimeAsyncSuspensionFacts
{
    private readonly Lazy<IRuntimeAsyncMemberResolver> _members = new(members);
    private readonly Dictionary<string, bool> _known = new(StringComparer.Ordinal);
    private readonly object _gate = new();

    public bool CanSuspend(MethodInstanceModel method)
    {
        ArgumentNullException.ThrowIfNull(method);
        lock (_gate)
        {
            if (!_known.TryGetValue(method.CanonicalName, out var known))
            {
                Explore(method);
                known = _known[method.CanonicalName];
            }
            return known;
        }
    }

    private sealed class Node(MethodInstanceModel method, int index)
    {
        public MethodInstanceModel Method { get; } = method;
        public int Index { get; } = index;
        public int LowLink { get; set; } = index;
        public bool OnStack { get; set; } = true;
        public bool Leaf { get; set; }
        public List<MethodInstanceModel> Callees { get; } = [];
        public int Next { get; set; }
    }

    // Tarjan's strongly connected components over the direct callees not yet known, from the method; each component, as
    // it closes (its callees' components already closed), takes its fact.
    private void Explore(MethodInstanceModel root)
    {
        var nodes = new Dictionary<string, Node>(StringComparer.Ordinal);
        var stack = new List<Node>();
        var path = new Stack<Node>();
        Node Open(MethodInstanceModel method)
        {
            var node = new Node(method, nodes.Count);
            nodes.Add(method.CanonicalName, node);
            stack.Add(node);
            Summarize(node);
            path.Push(node);
            return node;
        }
        Open(root);
        while (path.Count > 0)
        {
            var node = path.Peek();
            if (node.Next < node.Callees.Count)
            {
                var callee = node.Callees[node.Next++];
                if (_known.ContainsKey(callee.CanonicalName)) continue;
                if (!nodes.TryGetValue(callee.CanonicalName, out var reached))
                {
                    Open(callee);
                    continue;
                }
                if (reached.OnStack) node.LowLink = Math.Min(node.LowLink, reached.Index);
                continue;
            }
            path.Pop();
            if (path.Count > 0) path.Peek().LowLink = Math.Min(path.Peek().LowLink, node.LowLink);
            if (node.LowLink != node.Index) continue;
            // The component node roots: the stack from node up.
            var start = stack.LastIndexOf(node);
            var members = stack.GetRange(start, stack.Count - start);
            stack.RemoveRange(start, stack.Count - start);
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var member in members) names.Add(member.Method.CanonicalName);
            var suspends = false;
            foreach (var member in members)
            {
                member.OnStack = false;
                suspends |= member.Leaf;
                foreach (var callee in member.Callees)
                {
                    if (!names.Contains(callee.CanonicalName) && _known.TryGetValue(callee.CanonicalName, out var calleeSuspends))
                        suspends |= calleeSuspends;
                }
            }
            foreach (var member in members) _known[member.Method.CanonicalName] = suspends;
        }
    }

    // A method's own awaits: whether one is a leaf or a virtual call, and the methods its direct awaits call.
    private void Summarize(Node node)
    {
        CilMethodBody body;
        try
        {
            body = sources.Read(node.Method);
        }
        catch (CompilerException)
        {
            node.Leaf = true;
            return;
        }
        var awaits = RuntimeAsyncAwaits.Classify(body, _members.Value.AsyncHelpers);
        if (awaits.IsEmpty) return;
        Dictionary<int, CilInstruction>? byOffset = null;
        foreach (var awaited in awaits)
        {
            if (awaited.DirectCallOffset is not { } call)
            {
                node.Leaf = true;
                continue;
            }
            byOffset ??= Index(body);
            if (byOffset[call].Operand is CilOperand.MethodInstance { Value: var callee }) node.Callees.Add(callee);
            else node.Leaf = true;
        }
    }

    private static Dictionary<int, CilInstruction> Index(CilMethodBody body)
    {
        var byOffset = new Dictionary<int, CilInstruction>(body.Instructions.Length);
        foreach (var instruction in body.Instructions) byOffset[instruction.Offset] = instruction;
        return byOffset;
    }
}
