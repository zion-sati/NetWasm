// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.ControlFlow;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Analysis.ReceiverFlow;

// What the receiver flow engine reads of a program: its bodies, its dispatched calls with the targets known so far,
// and the methods code no body shows calls (boundaries) or the runtime enters (entries). A finished program answers
// once; the closure's ledger answers again at each settle, with more.
internal interface IReceiverFlowProgramView
{
    // Each method's body, by canonical name.
    IEntryLookup<string, ManagedMethodBody> MethodBodies { get; }

    IEnumerable<ManagedMethodBody> Bodies { get; }

    // The dispatched calls, by site key.
    IMembership<string> DispatchSites { get; }

    // A dispatched call's targets, with an identity that calls sharing one list share.
    IEntryLookup<string, DispatchTargetList> DispatchTargets { get; }

    // A list's targets now, by the identity DispatchTargets gave it; no entry for a list that does not grow.
    IEntryLookup<object, IReadOnlyCollection<DispatchTargetModel>> CurrentTargets { get; }

    IEnumerable<MethodInstanceModel> Boundaries { get; }

    IEnumerable<MethodInstanceModel> Entries { get; }

    // Methods whose address the program takes: those not seen made into a delegate the engine follows are boundaries.
    IEnumerable<MethodInstanceModel> Callables { get; }

}

// A dispatched call's target list: the identity calls sharing the list share, and its targets.
internal readonly record struct DispatchTargetList(object List, IReadOnlyCollection<DispatchTargetModel> Targets);

// A finished program, after the closure.
internal sealed class ReachableProgramFlowView : IReceiverFlowProgramView
{
    private readonly ReachableProgram _program;
    private readonly ReachabilityRoots _roots;
    private readonly Dictionary<string, ManagedMethodBody> _bodies = new(StringComparer.Ordinal);

    public ReachableProgramFlowView(ReachableProgram program, ReachabilityRoots roots)
    {
        _program = program ?? throw new ArgumentNullException(nameof(program));
        _roots = roots ?? throw new ArgumentNullException(nameof(roots));
        foreach (var body in program.Methods.Values.Concat(program.ConstructedMethods.Values))
            _bodies.TryAdd(body.Method.CanonicalName, body);
        MethodBodies = Lookups.Of(_bodies);
        DispatchSites = new Sites(program);
        DispatchTargets = new Targets(program);
    }

    public IEntryLookup<string, ManagedMethodBody> MethodBodies { get; }

    public IEnumerable<ManagedMethodBody> Bodies => _bodies.Values;

    public IMembership<string> DispatchSites { get; }

    public IEntryLookup<string, DispatchTargetList> DispatchTargets { get; }

    // A finished program's lists do not grow.
    public IEntryLookup<object, IReadOnlyCollection<DispatchTargetModel>> CurrentTargets { get; } =
        Lookups.NoEntries<object, IReadOnlyCollection<DispatchTargetModel>>();

    public IEnumerable<MethodInstanceModel> Boundaries
    {
        get
        {
            var keys = _roots.Methods.Concat(_roots.InvokedMethods).Concat(_program.Exports.Select(static export => export.Method));
            if (_program.EntryPoint is { } entry) keys = keys.Append(entry.Key);
            foreach (var key in keys)
            {
                if (_program.Methods.TryGetValue(key, out var body)) yield return body.Method;
            }
            foreach (var method in _program.NativeCallbacks.Values.Concat(_program.MethodDescriptors.Values)
                .Concat(_program.Finalizers.Values).Concat(_program.SharedObjectDispatch.Values)
                .Concat(_program.JavaScriptConversions.Values).Concat(_program.StructMarshallingConstructors.Values))
            {
                yield return method;
            }
            foreach (var method in ReceiverFlowBoundaries.Generated(_bodies, _program.Methods, _program.GeneratedCallees))
                yield return method;
        }
    }

    public IEnumerable<MethodInstanceModel> Entries => ReceiverFlowBoundaries.Entries(_bodies, _program.Methods,
        _program.StaticInitializers.Concat(_program.ModuleInitializers), _program.ConstructedStaticInitializers);

    public IEnumerable<MethodInstanceModel> Callables => _program.CallableMethods.Values;


    private sealed class Sites(ReachableProgram program) : IMembership<string>
    {
        public bool Contains(string key) => program.DispatchCallSites.ContainsKey(key);
    }

    // Each call's target array is its list: calls the program builder gave one list share it.
    private sealed class Targets(ReachableProgram program) : IEntryLookup<string, DispatchTargetList>
    {
        public bool TryGetValue(string key, out DispatchTargetList value)
        {
            if (program.DispatchCallSites.TryGetValue(key, out var site) &&
                System.Runtime.InteropServices.ImmutableCollectionsMarshal.AsArray(site.Targets) is { } array)
            {
                value = new(array, array);
                return true;
            }
            value = default;
            return false;
        }
    }
}

// The boundaries and entries both views share: what generated code and the runtime call.
internal static class ReceiverFlowBoundaries
{
    // A runtime-async method's resumption stub and lowered body, and the methods the closure recorded as called by
    // generated code.
    public static IEnumerable<MethodInstanceModel> Generated(IReadOnlyDictionary<string, ManagedMethodBody> bodies,
        IReadOnlyDictionary<EntityKey, ManagedMethodBody> definitions, IEnumerable<string> generatedCallees)
    {
        foreach (var body in bodies.Values)
        {
            var definition = body.Method.Definition;
            if (definition.RuntimeAsyncBodyOf is not null || definition.AsyncIteratorDisposeOf is not null) yield return body.Method;
            if (definition.RuntimeAsyncResumer is { } resumer && definitions.TryGetValue(resumer, out var stub)) yield return stub.Method;
            if (definition.RuntimeAsyncBody is { } lowered && definitions.TryGetValue(lowered, out var inner)) yield return inner.Method;
        }
        foreach (var name in generatedCallees)
        {
            if (bodies.TryGetValue(name, out var body)) yield return body.Method;
        }
    }

    // Static and module initializers, and every static constructor: the runtime runs them, with no arguments.
    public static IEnumerable<MethodInstanceModel> Entries(IReadOnlyDictionary<string, ManagedMethodBody> bodies,
        IReadOnlyDictionary<EntityKey, ManagedMethodBody> definitions, IEnumerable<EntityKey> initializers,
        IEnumerable<string> constructedInitializers)
    {
        foreach (var key in initializers)
        {
            if (definitions.TryGetValue(key, out var body)) yield return body.Method;
        }
        foreach (var name in constructedInitializers)
        {
            if (bodies.TryGetValue(name, out var body)) yield return body.Method;
        }
        foreach (var body in bodies.Values)
        {
            if (body.Method.Definition is { Name: ".cctor", IsStatic: true }) yield return body.Method;
        }
    }
}
