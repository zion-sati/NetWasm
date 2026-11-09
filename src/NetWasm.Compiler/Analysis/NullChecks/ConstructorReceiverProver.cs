using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.ControlFlow;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Analysis.NullChecks;

internal interface IConstructorReceiverProver
{
    // The program with the accesses of each proven constructor's this added to its
    // body's proof of non-null references.
    ReachableProgram Prove(ReachableProgram program);
}

internal static class ConstructorReceivers
{
    public static bool IsInstanceConstructor(MethodInstanceModel method) =>
        method.Definition is { Name: ".ctor", IsStatic: false } && !method.DeclaringType.IsValueType;
}

// A reference type's constructor runs on the object newobj allocated for it, or on
// the this of a constructor that chains to it with call, or with callvirt as F# does.
// Verifiable code can run it no other way, but IL can, with a call on any object, and
// so can function pointers, delegates and member execution. So a constructor's this
// is proven non-null when every reference to it in the program is newobj, or a call
// whose object newobj produced or that passes, unmodified, the this of a constructor
// whose own this is proven. A definition is judged as a whole, so one
// instantiation's call disproves them all.
internal sealed class ConstructorReceiverProver(
    IConstructedReceiverPlanner planner,
    ICalledMethodResolver calledMethods,
    IControlFlowGraphBuilder graphs,
    ITypedStackValidator stacks,
    bool enabled) : IConstructorReceiverProver
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
        // The constructors each constructor chains to with its own this.
        var chains = new Dictionary<EntityKey, HashSet<EntityKey>>();
        var thisAccesses = new List<(ManagedMethodBody Method, ImmutableHashSet<int> Offsets)>();
        foreach (var method in program.Methods.Values.Concat(program.ConstructedMethods.Values))
        {
            var constructor = ConstructorReceivers.IsInstanceConstructor(method.Method);
            var references = ConstructorReferences(method.Body).ToArray();
            if (!constructor && references.Length == 0) continue;
            var constructed = _planner.Plan(method.ControlFlow, thisIsNonNull: false);
            var withThis = constructor ? _planner.Plan(method.ControlFlow, thisIsNonNull: true) : constructed;
            foreach (var (instruction, target) in references)
            {
                // F# chains with callvirt, which only adds a null check, as no constructor is virtual.
                var direct = instruction.Operation is CilOperation.Call or CilOperation.CallVirtual;
                if (direct && constructed.Contains(instruction.Offset)) continue;
                if (direct && withThis.Contains(instruction.Offset))
                {
                    var caller = method.Method.Definition.Key;
                    if (!chains.TryGetValue(caller, out var callees)) chains[caller] = callees = [];
                    callees.Add(target.Definition.Key);
                    continue;
                }
                disproven.Add(target.Definition.Key);
            }
            if (constructor && withThis.Except(constructed) is { IsEmpty: false } offsets) thisAccesses.Add((method, offsets));
        }
        foreach (var target in Invoked(program))
        {
            if (ConstructorReceivers.IsInstanceConstructor(target)) disproven.Add(target.Definition.Key);
        }
        // A constructor chained to from one whose this may be null may get null too.
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

    // Each instruction that names a reference type's instance constructor other
    // than by newobj, and the constructor it names.
    private IEnumerable<(CilInstruction Instruction, MethodInstanceModel Target)> ConstructorReferences(CilMethodBody body)
    {
        foreach (var instruction in body.Instructions)
        {
            var target = instruction.Operation switch
            {
                CilOperation.Call or CilOperation.CallVirtual or CilOperation.LoadFunction or
                    CilOperation.LoadVirtualFunction => _calledMethods.Resolve(instruction),
                // A method handle reaches reflection, which can invoke the method on any object.
                CilOperation.LoadMethodToken => ((CilOperand.MethodInstance)instruction.Operand!).Value,
                _ => null,
            };
            if (target is not null && ConstructorReceivers.IsInstanceConstructor(target))
            {
                yield return (instruction, target);
            }
        }
    }

    // The methods delegates bind to and member execution invokes, which may receive any object.
    private static IEnumerable<MethodInstanceModel> Invoked(ReachableProgram program)
    {
        foreach (var binding in program.DelegateBindings) yield return binding.Target;
        foreach (var plan in program.MemberExecution.Methods.Values)
        {
            yield return plan.Descriptor;
            foreach (var target in plan.Targets) yield return target.Method;
        }
        if (program.MemberExecution.DelegateInvocation is { } invocation)
        {
            foreach (var method in invocation.Methods.Values) yield return method;
        }
    }
}
