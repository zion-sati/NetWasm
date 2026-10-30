using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using NetWasm.Compiler.ComponentModel.Optimization;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Core.Types;
using NetWasm.Compiler.Metadata;

namespace NetWasm.Compiler.Analysis.Inlining;

internal sealed class ManagedInliningResolver(
    IMethodBodyReader methodBodies,
    IMethodSpecializer specializer,
    ICalledMethodResolver calledMethods,
    IManagedInliningBodyValidator bodyValidator,
    ManagedMethodInliningPolicy policy,
    IMethodInitializationTriggerClassifier initializationTriggers,
    IRuntimeIntrinsicRegistry intrinsics,
    int smallBodyInstructionLimit,
    int leafBodyInstructionLimit,
    int loopBodyInstructionLimit) : IManagedInliningResolver
{
    private readonly IMethodBodyReader _methodBodies = methodBodies ??
        throw new ArgumentNullException(nameof(methodBodies));
    private readonly IMethodSpecializer _specializer = specializer ??
        throw new ArgumentNullException(nameof(specializer));
    private readonly ICalledMethodResolver _calledMethods = calledMethods ??
        throw new ArgumentNullException(nameof(calledMethods));
    private readonly IManagedInliningBodyValidator _bodyValidator = bodyValidator ??
        throw new ArgumentNullException(nameof(bodyValidator));
    private readonly IMethodInitializationTriggerClassifier _initializationTriggers = initializationTriggers ??
        throw new ArgumentNullException(nameof(initializationTriggers));
    private readonly IRuntimeIntrinsicRegistry _intrinsics = intrinsics ??
        throw new ArgumentNullException(nameof(intrinsics));
    private readonly ManagedMethodInliningPolicy _policy = policy switch
    {
        ManagedMethodInliningPolicy.Disabled or
        ManagedMethodInliningPolicy.Speed or
        ManagedMethodInliningPolicy.Loops or
        ManagedMethodInliningPolicy.Aggressive => policy,
        _ => throw new ArgumentOutOfRangeException(nameof(policy)),
    };
    private readonly int _smallBodyInstructionLimit = smallBodyInstructionLimit >= 0
        ? smallBodyInstructionLimit
        : throw new ArgumentOutOfRangeException(nameof(smallBodyInstructionLimit));
    private readonly int _leafBodyInstructionLimit = leafBodyInstructionLimit >= 0
        ? leafBodyInstructionLimit
        : throw new ArgumentOutOfRangeException(nameof(leafBodyInstructionLimit));
    private readonly int _loopBodyInstructionLimit = loopBodyInstructionLimit >= 0
        ? loopBodyInstructionLimit
        : throw new ArgumentOutOfRangeException(nameof(loopBodyInstructionLimit));
    // Whether a callee qualifies depends only on the callee, so each is examined once.
    private readonly ConcurrentDictionary<string, Candidate?> _candidates = new(StringComparer.Ordinal);

    // .NET's JIT always inlines a legal callee of up to 16 IL bytes, about
    // eight instructions. O3 trades size for speed, and sixteen measured faster
    // under Wasmtime than eight.
    internal const int SmallBodyInstructionLimit = 16;

    // .NET's JIT also inlines larger callees when inlining pays. A straight-line
    // leaf called inside a loop pays: it makes no calls, so inlining it removes the
    // loop's call and lets the loop keep values in registers, and without branches
    // it leaves the loop in the shape bounds-check elimination recognizes. Outside
    // a loop the call runs once, and the extra code only raises the register
    // pressure of the rest of its caller. A tuple swap of two array elements is
    // seventeen instructions.
    internal const int LeafBodyInstructionLimit = 32;

    // .NET's JIT weighs callees of up to 128 IL bytes and seven basic blocks
    // (JitExtDefaultPolicyMaxIL and JitExtDefaultPolicyMaxBB), and more at a
    // profiled hot call site. A call inside a loop is this compiler's hot call
    // site, so a callee there may also branch forward, within about 128 IL
    // bytes: fifty-six instructions at the 2.3 bytes per instruction of a
    // shading helper with a clamp. Its calls must be ones the next round
    // inlines, so the loop keeps no call of its own. A branching callee no
    // larger than a leaf stays a call: Binaryen inlines it after turning its
    // branches into selects, which kept a set-membership test branch-free that
    // this inliner left branching (sparse-set took 32% longer).
    internal const int LoopBodyInstructionLimit = 56;
    internal const int LoopBodyBranchLimit = 6;

    public ManagedInliningPlan? Resolve(ManagedInliningRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (_policy == ManagedMethodInliningPolicy.Disabled ||
            request.Call.Operation is not (CilOperation.Call or CilOperation.CallVirtual or CilOperation.NewObject) ||
            _calledMethods.Resolve(request.Call) is not { } method ||
            !method.Definition.HasManagedBody ||
            method.Definition.IsNoInlining ||
            // The compiler replaces these calls; their bodies are placeholders or stubs.
            _intrinsics.TryGetIntrinsic(method.Definition.Key, out _) ||
            method.Definition.JSImport is not null || method.Definition.WitImport is not null ||
            method.Definition.NativeImport is not null ||
            _initializationTriggers.Classify(method.Definition) ||
            method.CanonicalName == request.Caller.CanonicalName ||
            !HasSupportedCallMetadata(request.Call, method))
        {
            return null;
        }

        var candidate = _candidates.GetOrAdd(method.CanonicalName, _ => Examine(method));
        // The priority shapes are unbounded in size, so calls exposed by an
        // earlier round take only small and explicitly aggressive callees, and
        // not allocating ones: a lazily initialized getter inlined through its
        // allocating initializer put that cold path into every caller.
        if (candidate is null ||
            !(_policy == ManagedMethodInliningPolicy.Aggressive
                // Only what the callee asks for.
                ? method.Definition.IsAggressiveInlining
                : _policy == ManagedMethodInliningPolicy.Loops
                // A field accessor inlines to the load the call's receiver
                // check already precedes, so it only shrinks its caller.
                ? method.Definition.IsAggressiveInlining || candidate.Accessor ||
                  request.InLoop && (candidate.Small && !(request.Nested && candidate.Allocates) ||
                                     candidate.Leaf || candidate.LoopBody)
                : candidate.Small && !(request.Nested && candidate.Allocates) ||
                  (candidate.Leaf || candidate.LoopBody) && request.InLoop ||
                  (request.Nested ? method.Definition.IsAggressiveInlining : candidate.Priority)) ||
            // A callvirt checks its receiver for null before the callee runs.
            !method.Definition.IsStatic && request.Call.Operation == CilOperation.CallVirtual &&
            !DereferencesReceiverFirst(candidate.Plan.Body))
        {
            return null;
        }
        return candidate.Plan;
    }

    private Candidate? Examine(MethodInstanceModel method)
    {
        var body = _specializer.Rewrite(_methodBodies.ReadMethodBody(method)) with
        {
            MethodInstance = method,
        };
        if (!IsSupportedBody(method, body))
        {
            return null;
        }
        var accessor = !method.Definition.IsStatic && IsReferenceFieldAccessor(body);
        var priority = method.Definition.IsStatic
            ? IsPriorityCandidate(method, body)
            : method.Definition.IsAggressiveInlining || accessor;
        var small = IsSmall(body);
        var leaf = IsLeaf(body);
        var loopBody = !leaf && IsLoopBody(body);
        return priority || small || leaf || loopBody
            ? new(new(method, body) { InstructionEntryStacks = _bodyValidator.Validate(body) }, priority, accessor, small,
                leaf, loopBody, Enumerable.Range(0, body.Instructions.Length).Any(index => Allocates(body, index)))
            : null;
    }

    private sealed record Candidate(
        ManagedInliningPlan Plan, bool Priority, bool Accessor, bool Small, bool Leaf, bool LoopBody, bool Allocates);

    // A straight-line leaf makes no calls, not even to methods the compiler
    // replaces, since some of those allocate. It does not branch, allocate or
    // throw: without a branch a throw is unconditional, and inlining a throw
    // helper puts its cold path into every caller, as .NET's JIT avoids. It may
    // construct a value, as an operator returning a new struct does, when the
    // next round inlines the constructor: that only initializes storage.
    private bool IsLeaf(CilMethodBody body) =>
        body.Instructions.Count(static instruction => instruction.Operation != CilOperation.Nop) <= _leafBodyInstructionLimit &&
        Enumerable.Range(0, body.Instructions.Length).All(index =>
            body.Instructions[index] is var instruction &&
            (instruction.Operation is not (
                CilOperation.Call or CilOperation.CallVirtual or CilOperation.NewObject or CilOperation.Throw) ||
                instruction.Operation == CilOperation.NewObject && InlinedInTheNextRound(instruction)) &&
            !Allocates(body, index) &&
            instruction.Operand is not (CilOperand.BranchTarget or CilOperand.SwitchTargets));

    // A loop body is larger than a leaf and may branch forward but not loop,
    // and neither allocates nor throws; each call it makes is to an aggressive
    // callee or to a small one that does not allocate, which the next round
    // inlines.
    private bool IsLoopBody(CilMethodBody body) =>
        body.Instructions.Count(static instruction => instruction.Operation != CilOperation.Nop) is var size &&
        size > _leafBodyInstructionLimit && size <= _loopBodyInstructionLimit &&
        body.Instructions.Count(static instruction => instruction.Operand is CilOperand.BranchTarget or CilOperand.SwitchTargets) <=
            LoopBodyBranchLimit &&
        Enumerable.Range(0, body.Instructions.Length).All(index =>
            body.Instructions[index] is var instruction &&
            instruction.Operation != CilOperation.Throw && !Allocates(body, index) &&
            instruction.Operand switch
            {
                CilOperand.BranchTarget target => target.Offset > instruction.Offset,
                CilOperand.SwitchTargets targets => targets.Offsets.All(offset => offset > instruction.Offset),
                _ => true,
            } &&
            (instruction.Operation is not (CilOperation.Call or CilOperation.CallVirtual or CilOperation.NewObject) ||
                InlinedInTheNextRound(instruction)));

    // A value type's constructor counts as such a call: newobj runs it on
    // storage the caller provides.
    private bool InlinedInTheNextRound(CilInstruction call) =>
        _calledMethods.Resolve(call) is { Definition: { HasManagedBody: true, IsNoInlining: false } definition } target &&
        (call.Operation == CilOperation.Call ||
            call.Operation == CilOperation.NewObject && target.DeclaringType.IsValueType) &&
        !_intrinsics.TryGetIntrinsic(definition.Key, out _) &&
        (definition.IsAggressiveInlining ||
            _methodBodies.ReadMethodBody(target) is var body && CountsAsSmall(body) &&
            !Enumerable.Range(0, body.Instructions.Length).Any(index => Allocates(body, index)));

    // A wrapper of a large explicit priority callee is as large as that callee
    // once the priority tier composes it, so it does not count as small. A
    // small callee composes like any other small callee in a later round, and
    // a call that the compiler replaces with instructions stays small.
    private bool IsSmall(CilMethodBody body) =>
        CountsAsSmall(body) &&
        !body.Instructions.Any(instruction => instruction.Operation is CilOperation.Call or CilOperation.CallVirtual &&
            _calledMethods.Resolve(instruction) is { } target && IsExplicitPriorityCandidate(target) &&
            !_intrinsics.TryGetIntrinsic(target.Definition.Key, out _) &&
            !(target.Definition.HasManagedBody && CountsAsSmall(_methodBodies.ReadMethodBody(target))));

    private bool CountsAsSmall(CilMethodBody body) =>
        body.Instructions.Count(static instruction => instruction.Operation != CilOperation.Nop) <= _smallBodyInstructionLimit;

    private bool Allocates(CilMethodBody body, int index) => body.Instructions[index].Operation switch
    {
        // Constructing a value type initializes storage in place, and an
        // exception constructed for a throw is the cold path. A supported
        // body ends in a return, so an instruction follows.
        CilOperation.NewObject => _calledMethods.Resolve(body.Instructions[index]) is not { DeclaringType.IsValueType: true } &&
            body.Instructions[index + 1].Operation != CilOperation.Throw,
        CilOperation.NewArray or CilOperation.NewRectangularArray or CilOperation.Box => true,
        _ => false,
    };

    // Inlined code keeps the callvirt null check's order only when its first
    // effect, after pure pushes, dereferences the receiver.
    // The receiver faults first when the body's first effect reads or writes
    // one of its fields, makes a callvirt on it, which checks it, or calls a
    // method on it that does either first, as reading an auto-property does.
    private bool DereferencesReceiverFirst(CilMethodBody body, int depth = 0)
    {
        var operations = body.Instructions.Where(static instruction => instruction.Operation != CilOperation.Nop).ToArray();
        // A supported body ends in ret, so an effect always exists.
        var effect = Array.FindIndex(operations, static instruction =>
            instruction.Operation is not (CilOperation.LoadArgument or CilOperation.LoadInt32 or CilOperation.LoadNull));
        var instruction = operations[effect];
        return instruction.Operation switch
        {
            CilOperation.LoadField or CilOperation.LoadFieldAddress => IsReceiver(operations, effect - 1),
            CilOperation.StoreField => IsReceiver(operations, effect - 2),
            CilOperation.Call or CilOperation.CallVirtual =>
                _calledMethods.Resolve(instruction) is
                { Definition: { IsStatic: false, JSImport: null, NativeImport: null, WitImport: null } definition } target &&
                !target.DeclaringType.IsValueType && !_intrinsics.TryGetIntrinsic(definition.Key, out _) &&
                IsReceiver(operations, effect - 1 - target.Signature.ParameterTypes.Length) &&
                (instruction.Operation == CilOperation.CallVirtual ||
                    depth < MaximumReceiverCallDepth && definition.HasBody &&
                    DereferencesReceiverFirst(_methodBodies.ReadMethodBody(target), depth + 1)),
            _ => false,
        };
    }

    private const int MaximumReceiverCallDepth = 2;

    private static bool IsReceiver(CilInstruction[] operations, int index) =>
        index >= 0 && operations[index] is { Operation: CilOperation.LoadArgument, Operand: CilOperand.Index { Value: 0 } };

    private static bool HasSupportedCallMetadata(
        CilInstruction call,
        MethodInstanceModel method)
    {
        if (method.Definition.IsStatic)
        {
            return call.Operation == CilOperation.Call;
        }
        // Constructing a value type initializes a local; an object needs an allocation first.
        if (call.Operation == CilOperation.NewObject)
        {
            return method.DeclaringType.IsValueType;
        }

        // A value-type instance is passed by address to a direct call.
        return method.DeclaringType.IsValueType
            ? call.Operation == CilOperation.Call
            : call.Operation == CilOperation.Call ||
                !method.Definition.IsVirtual ||
                method.Definition.IsFinal;
    }

    private static bool IsPriorityCandidate(
        MethodInstanceModel method,
        CilMethodBody body) =>
        IsExplicitPriorityCandidate(method) ||
        IsAggregateAddressForwardingWrapper(method, body);

    private static bool IsExplicitPriorityCandidate(MethodInstanceModel method) =>
        method.Definition.IsAggressiveInlining ||
        method.Signature.ParameterSignatureTypes.Any(
            static type => type.Shape == CliTypeShape.ManagedByReference);

    private static bool IsAggregateAddressForwardingWrapper(
        MethodInstanceModel method,
        CilMethodBody body)
    {
        var signature = method.Signature;
        if (signature.ReturnSignatureType.StackKind != CliValueKind.ValueType ||
            !body.LocalSignatureTypes.IsEmpty)
        {
            return false;
        }

        var operations = body.Instructions
            .Where(static instruction => instruction.Operation != CilOperation.Nop)
            .ToArray();
        if (operations.Length < 4 ||
            operations[^2] is not
            {
                Operation: CilOperation.LoadArgument,
                Operand: CilOperand.Index returnIndex,
            } ||
            !IsAggregateParameter(signature, returnIndex.Value) ||
            !signature.ParameterSignatureTypes[returnIndex.Value].Equals(
                signature.ReturnSignatureType))
        {
            return false;
        }

        var hasAddressProjection = false;
        var hasCall = false;
        foreach (var instruction in operations[..^2])
        {
            switch (instruction)
            {
                case
                {
                    Operation: CilOperation.LoadArgumentAddress,
                    Operand: CilOperand.Index index,
                } when IsAggregateParameter(signature, index.Value):
                    hasAddressProjection = true;
                    break;
                case { Operation: CilOperation.LoadInt32 }:
                    break;
                case { Operation: CilOperation.Call }:
                    hasCall = true;
                    break;
                default:
                    return false;
            }
        }

        return hasAddressProjection && hasCall;
    }

    private static bool IsAggregateParameter(
        MethodSignatureModel signature,
        int index) =>
        (uint)index < (uint)signature.ParameterSignatureTypes.Length &&
        signature.ParameterSignatureTypes[index].StackKind == CliValueKind.ValueType;

    private static bool IsSupportedBody(
        MethodInstanceModel method,
        CilMethodBody body)
    {
        if (body.Method.Key != method.Definition.Key ||
            !body.ExceptionRegions.IsEmpty ||
            body.Instructions.IsEmpty ||
            body.Instructions[^1].Operation != CilOperation.Return ||
            body.LocalSignatureTypes.Length != body.Locals.Length ||
            // A constructed method's body is decoded in its own generic
            // context, so it moves into another method only when closed.
            body.LocalSignatureTypes.Any(static type => type.ContainsGenericParameters) ||
            body.Instructions.Any(static instruction => IsOpen(instruction.Operand)))
        {
            return false;
        }

        var offsets = body.Instructions
            .Select(static instruction => instruction.Offset)
            .ToHashSet();
        return body.Instructions.All(instruction =>
            // A throw leaves the inlined body the way it leaves a call.
            instruction.Operation is not (
                CilOperation.Rethrow or
                CilOperation.Leave or
                CilOperation.EndFinally or
                CilOperation.EndFilter or
                CilOperation.LocalAllocate) &&
            HasValidTargets(instruction.Operand, offsets));
    }

    private static bool IsOpen(CilOperand operand) => operand switch
    {
        CilOperand.TypeIdentity type => type.Value.ContainsGenericParameters,
        CilOperand.ExactType type => type.Value.ContainsGenericParameters,
        CilOperand.MethodInstance method => method.Value.DeclaringType.ContainsGenericParameters ||
            method.Value.MethodArguments.Any(static type => type.ContainsGenericParameters),
        CilOperand.FieldInstance field => field.Value.DeclaringType.ContainsGenericParameters ||
            field.Value.FieldType.ContainsGenericParameters,
        _ => false,
    };

    private static bool HasValidTargets(
        CilOperand operand,
        HashSet<int> offsets) => operand switch
        {
            CilOperand.BranchTarget target => offsets.Contains(target.Offset),
            CilOperand.SwitchTargets targets =>
                targets.Offsets.All(offsets.Contains),
            _ => true,
        };

    private static bool IsReferenceFieldAccessor(CilMethodBody body)
    {
        var operations = body.Instructions
            .Where(static instruction => instruction.Operation != CilOperation.Nop)
            .ToArray();
        return operations is
        [
        {
            Operation: CilOperation.LoadArgument,
            Operand: CilOperand.Index { Value: 0 },
        },
        { Operation: CilOperation.LoadField or CilOperation.LoadFieldAddress },
        { Operation: CilOperation.Return },
        ];
    }
}
