// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Analysis.ReceiverFlow;

// An abstract object the analysis follows: an object of one exact constructed type, or a delegate with its target.
internal abstract record FlowObject
{
    private FlowObject()
    {
    }

    public abstract CliTypeIdentity Type { get; }

    public sealed record Exact(CliTypeIdentity Value) : FlowObject
    {
        public override CliTypeIdentity Type => Value;

        public bool Equals(Exact? other) => other is not null && Value.Equals(other.Value);

        public override int GetHashCode() => Value.GetHashCode();
    }

    public sealed record Delegate(CliTypeIdentity DelegateType, MethodInstanceModel Target) : FlowObject
    {
        public override CliTypeIdentity Type => DelegateType;

        public bool Equals(Delegate? other) => other is not null && DelegateType.Equals(other.DelegateType) &&
            Target.CanonicalName == other.Target.CanonicalName;

        public override int GetHashCode() => HashCode.Combine(DelegateType.GetHashCode(),
            Target.CanonicalName.GetHashCode(StringComparison.Ordinal));
    }
}

internal enum FlowNodeKind
{
    // A method instance's argument by its IL number (an instance method's this is 0).
    Parameter,
    Return,
    // A runtime-async method's returned T, which its ret carries while callers see a task.
    AsyncReturn,
    Field,
    Local,
    // The value one store to a local writes, by method and the store's offset.
    LocalStore,
    // A value on a block's entry stack, by block and slot.
    StackEntry,
    // A call's result, by caller and IL offset.
    Result,
    // A dispatched call's or delegate invoke's receiver, by site.
    Receiver,
    // A dispatched call's or delegate invoke's argument, by site and declared parameter.
    Argument,
    // Where values go that code the analysis does not see may read: a delegate reaching it has top parameters.
    Escape,
}

internal readonly record struct FlowNodeKey(FlowNodeKind Kind, string Owner, int Index = 0, int Slot = 0)
{
    public static FlowNodeKey Escape { get; } = new(FlowNodeKind.Escape, "");
}

// Anything assignable to every type of the conjunction (no types: anything), and why the analysis stopped there.
internal sealed record FlowBound(ImmutableArray<CliTypeIdentity> Types, string Cause)
{
    public FlowBound And(CliTypeIdentity type) =>
        Types.Any(existing => existing.CanonicalName == type.CanonicalName) ? this : this with { Types = Types.Add(type) };

    public static FlowBound Of(CliTypeIdentity? type, string cause) =>
        new(type is null || type.ContainsGenericParameters ? [] : [type], cause);
}

internal abstract record FlowSource
{
    private FlowSource()
    {
    }

    public sealed record Object(FlowObject Value) : FlowSource;

    public sealed record Top(FlowBound Bound) : FlowSource;

    // The values of a node that pass every cast on the way.
    public sealed record Node(FlowNodeKey Key, ImmutableArray<CliTypeIdentity> Casts) : FlowSource;
}

// A source flowing into a node, with the node's declared type (its bound when its set passes the cap).
internal sealed record FlowEdge(FlowSource Source, FlowNodeKey Target, CliTypeIdentity? TargetType);

internal enum FlowSiteKind
{
    Dispatch,
    DelegateInvoke,
}

// A call whose targets depend on its receiver: the receiver and argument nodes and the result node, when the call
// returns a reference.
internal sealed record FlowSite(
    FlowSiteKind Kind,
    string Key,
    MethodInstanceModel Declaration,
    int ArgumentCount,
    FlowNodeKey? Result);

// What one method body contributes.
internal sealed record ReceiverFlowSummary(
    MethodInstanceModel Method,
    ImmutableArray<FlowEdge> Edges,
    ImmutableArray<FlowSite> Sites,
    // Methods it calls directly or makes a delegate to: reachable when it is.
    ImmutableArray<MethodInstanceModel> Reached,
    // Methods whose address it takes other than to make a delegate the analysis follows.
    ImmutableArray<MethodInstanceModel> AddressEscapes,
    // Unsafe.As<T>(object) calls: the type and the node holding the reinterpreted value.
    ImmutableArray<(CliTypeIdentity Type, FlowNodeKey Source)> Reinterpretations);
