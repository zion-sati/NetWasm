// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.ControlFlow;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Analysis.NullChecks;

internal interface IReceiverProver
{
    // The program with the accesses of each proven method's this added to its
    // body's proof of non-null references.
    ReachableProgram Prove(ReachableProgram program);
}

internal static class ConstructorReceivers
{
    public static bool IsInstanceConstructor(MethodInstanceModel method) =>
        method.Definition is { Name: ".ctor", IsStatic: false } && !method.DeclaringType.IsValueType;

    // An instance method of a reference type, constructors included: its this is an object reference.
    public static bool IsClassInstanceMethod(MethodInstanceModel method) =>
        method.Definition is { IsStatic: false } && !method.DeclaringType.IsValueType;
}

// An instance method of a reference type runs on a non-null this when every way
// the program reaches it passes one. callvirt checks its object before the call,
// and newobj makes a new one for a constructor. A direct call passes an object
// newobj produced, or, unmodified, the this of a method whose own this is proven,
// as `this.Ensure(4)` and a chained constructor do. IL can also call a method
// directly on any object, and function pointers, method handles, delegates and
// member execution can reach one with any object, so a reference of any other
// kind disproves the method it names; a definition is judged as a whole, so one
// instantiation's reference disproves them all. An override is reached by
// dispatch on an object, unless a function pointer ldvirtftn made is called
// directly rather than bound to a delegate: then no virtual method is proven.
internal sealed class ReceiverProver(
    IConstructedReceiverPlanner planner,
    ICalledMethodResolver calledMethods,
    IControlFlowGraphBuilder graphs,
    ITypedStackValidator stacks,
    bool enabled) : IReceiverProver
{
    private readonly IConstructedReceiverPlanner _planner = planner ?? throw new ArgumentNullException(nameof(planner));
    private readonly ICalledMethodResolver _calledMethods = calledMethods ??
        throw new ArgumentNullException(nameof(calledMethods));
    private readonly IControlFlowGraphBuilder _graphs = graphs ?? throw new ArgumentNullException(nameof(graphs));
    private readonly ITypedStackValidator _stacks = stacks ?? throw new ArgumentNullException(nameof(stacks));

    public ReachableProgram Prove(ReachableProgram program)
    {
        ArgumentNullException.ThrowIfNull(program);
        if (!enabled) return program;
        var disproven = new HashSet<EntityKey>();
        // The methods each method calls directly with its own this.
        var chains = new Dictionary<EntityKey, HashSet<EntityKey>>();
        var thisAccesses = new List<(ManagedMethodBody Method, ImmutableHashSet<int> Offsets)>();
        var bodies = program.Methods.Values.Concat(program.ConstructedMethods.Values).ToArray();
        var loosePointers = false;
        foreach (var method in bodies)
        {
            var instance = ConstructorReceivers.IsClassInstanceMethod(method.Method);
            var references = InstanceMethodReferences.Of(method.Body, _calledMethods).ToArray();
            if (!instance && references.Length == 0) continue;
            var constructed = _planner.Plan(method.ControlFlow, thisIsNonNull: false);
            var withThis = instance ? _planner.Plan(method.ControlFlow, thisIsNonNull: true) : constructed;
            foreach (var (index, target) in references)
            {
                var instruction = method.Body.Instructions[index];
                switch (instruction.Operation)
                {
                    // callvirt checks its object before the call.
                    case CilOperation.CallVirtual:
                        continue;
                    case CilOperation.Call when constructed.Contains(instruction.Offset):
                        continue;
                    case CilOperation.Call when withThis.Contains(instruction.Offset):
                        var caller = method.Method.Definition.Key;
                        if (!chains.TryGetValue(caller, out var callees)) chains[caller] = callees = [];
                        callees.Add(target.Definition.Key);
                        continue;
                    case CilOperation.LoadVirtualFunction when !BindsDelegate(method.Body.Instructions, index):
                        loosePointers = true;
                        break;
                }
                disproven.Add(target.Definition.Key);
            }
            if (instance && withThis.Except(constructed) is { IsEmpty: false } offsets) thisAccesses.Add((method, offsets));
        }
        foreach (var target in InstanceMethodReferences.Invoked(program))
        {
            if (ConstructorReceivers.IsClassInstanceMethod(target)) disproven.Add(target.Definition.Key);
        }
        if (loosePointers)
        {
            disproven.UnionWith(bodies.Where(method => method.Method.Definition.IsVirtual)
                .Select(method => method.Method.Definition.Key));
        }
        // A method called with the this of one whose this may be null may get null too.
        var pending = new Queue<EntityKey>(disproven);
        while (pending.TryDequeue(out var caller))
        {
            if (!chains.TryGetValue(caller, out var callees)) continue;
            foreach (var callee in callees)
            {
                if (disproven.Add(callee)) pending.Enqueue(callee);
            }
        }
        var methods = program.Methods.ToBuilder();
        var constructedMethods = program.ConstructedMethods.ToBuilder();
        foreach (var (method, offsets) in thisAccesses)
        {
            if (disproven.Contains(method.Method.Definition.Key)) continue;
            var proven = new ManagedMethodBody(method.Method,
                _stacks.Validate(_graphs.Build(NonNullProof.Add(method.Body, offsets))));
            if (program.Methods.TryGetValue(method.Method.Definition.Key, out var entity) && ReferenceEquals(entity, method))
            {
                methods[method.Method.Definition.Key] = proven;
            }
            else
            {
                constructedMethods[method.Method.CanonicalName] = proven;
            }
        }
        return program with { Methods = methods.ToImmutable(), ConstructedMethods = constructedMethods.ToImmutable() };
    }

    // Whether the function pointer an ldvirtftn makes goes straight into a delegate's
    // constructor, which takes an object and a pointer, as `new Action(x.Virtual)` does.
    private bool BindsDelegate(ImmutableArray<CilInstruction> instructions, int index) =>
        index + 1 < instructions.Length && instructions[index + 1].Operation == CilOperation.NewObject &&
        _calledMethods.Resolve(instructions[index + 1]) is
        {
            Signature.ParameterTypes: [CliValueKind.ManagedReference, CliValueKind.NativeInt],
        };
}
