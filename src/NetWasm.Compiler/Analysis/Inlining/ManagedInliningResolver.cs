// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.ComponentModel.Optimization;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Core.Types;
using NetWasm.Compiler.Metadata;

namespace NetWasm.Compiler.Analysis.Inlining;

internal sealed class ManagedInliningResolver(
    ICalleeMethodBodyReader methodBodies,
    IMethodSpecializer specializer,
    ICalledMethodResolver calledMethods,
    IManagedInliningBodyValidator bodyValidator,
    ManagedMethodInliningPolicy policy,
    IMethodInitializationTriggerClassifier initializationTriggers,
    IRuntimeIntrinsicRegistry intrinsics,
    int smallBodyInstructionLimit,
    int leafBodyInstructionLimit,
    int loopBodyInstructionLimit,
    int leafBodyILBytes = 0) : IManagedInliningResolver
{
    private readonly ICalleeMethodBodyReader _methodBodies = methodBodies ??
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
    private readonly int _leafBodyILBytes = leafBodyILBytes >= 0
        ? leafBodyILBytes
        : throw new ArgumentOutOfRangeException(nameof(leafBodyILBytes));
    // Whether a callee qualifies depends only on the callee, so each is examined once.
    private readonly ConcurrentDictionary<string, Candidate?> _candidates = new(StringComparer.Ordinal);

    // .NET's JIT always inlines a legal callee of up to 16 IL bytes, about
    // eight instructions. O3 trades size for speed, and sixteen measured faster
    // under Wasmtime than eight.
    internal const int SmallBodyInstructionLimit = 16;

    // The JIT's always-inline size (DEFAULT_ALWAYS_INLINE_SIZE): a callee of no more IL than this inlines at any call
    // site, since its body is about the size of the call it replaces. It holds where only loops otherwise inline, as at
    // Oz, for a callee that neither allocates nor throws: the JIT does not inline a throw helper either.
    internal const int AlwaysInlineILBytes = 16;

    // .NET's JIT also inlines larger callees when inlining pays. A straight-line
    // leaf called inside a loop pays: it makes no calls, so inlining it removes the
    // loop's call and lets the loop keep values in registers, and without branches
    // it leaves the loop in the shape bounds-check elimination recognizes. Outside
    // a loop the call runs once, and the extra code only raises the register
    // pressure of the rest of its caller. A tuple swap of two array elements is
    // seventeen instructions.
    internal const int LeafBodyInstructionLimit = 32;

    // A leaf is also within the 128 IL bytes .NET's JIT weighs a callee by at a loop's call site
    // (JitExtDefaultPolicyMaxIL): a pixel helper that packs four channels in 58 instructions is 112 bytes. It still
    // makes no calls and does not branch, so inlining it only removes the loop's call.
    internal const int LeafBodyILBytes = 128;

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

    // A callee that takes an argument by reference keeps the caller's value in memory across the call, and inlining
    // it lets the caller keep that value in a register, so the priority tier takes one anywhere within the 100 bytes
    // of IL past which .NET's JIT does not inline a callee (DEFAULT_MAX_INLINE_SIZE): Dhrystone's Proc6, 62
    // instructions in 72 bytes that a constant argument mostly folds away, ran 1.8 times as long as a call under the
    // 56 instructions this budget used to count. Past the budget it takes one only where the call, outside the
    // caller's loops, passes the address of one of the caller's locals: an Adler-32 checksum's running sums then stay
    // in registers through its loop. Inside a loop it takes one only where the callee reads or writes a local of the
    // caller inside a loop of its own, through the parameter the call passes the local's address in: inlined, every
    // one of those accesses is a register access, or a struct's field one once the struct is split into locals. A root
    // finder called once per iteration with its two estimates by reference, which its own loop updates, ran 3.5 times
    // as long as a call, and a logic test whose loops update a work area struct's fields twice as long. Elsewhere the
    // callee reaches the local only once or twice, as a decoder's residual parser loads its left context before its
    // loop and stores it after, or the call passes an address the caller holds, or one into an array or a field, which
    // inlining keeps in memory all the same; inlining then only raises the register pressure of the whole caller. A
    // VP8 decoder ran 1.5 to 1.8 times as long with its macroblock routines inlined into the frame loop, and a VP8L
    // decoder 1.6 times as long with its pixel loop inlined into the method that reads its codes.
    internal const int ByReferenceBodyILBytes = 100;

    // What the priority tier may compose into a wrapper that still counts as small under the speed policy (see IsSmall).
    internal const int ComposedBodyInstructionLimit = LoopBodyInstructionLimit;

    public ManagedInliningPlan? Resolve(ManagedInliningRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return _policy == ManagedMethodInliningPolicy.Disabled ||
            request.Call.Operation is not (CilOperation.Call or CilOperation.CallVirtual or CilOperation.NewObject) ||
            _calledMethods.Resolve(request.Call) is not { } method
            ? null
            : Resolve(request, method, forwarded: false);
    }

    // The plan for the call as a call to the method; a forwarder's call resolves once more, as a call to its callee.
    private ManagedInliningPlan? Resolve(ManagedInliningRequest request, MethodInstanceModel method, bool forwarded)
    {
        if (!method.Definition.HasManagedBody ||
            method.Definition.IsNoInlining ||
            // The compiler replaces these calls; their bodies are placeholders or stubs.
            _intrinsics.TryGetIntrinsic(method.Definition.Key, out _) ||
            method.Definition.JSImport is not null || method.Definition.WitImport is not null ||
            method.Definition.NativeImport is not null ||
            _initializationTriggers.Classify(method.Definition) && !TriggeredByCaller(request.Caller, method) ||
            // Reachability reads these calls to emit the runtime facts they read.
            ReachabilityMarkers.IsMarker(method) ||
            // The attribute query lowering reads these calls' targets and filters.
            Attributes.AttributeQueryApis.IsQuery(method) ||
            method.CanonicalName == request.Caller.CanonicalName ||
            !HasSupportedCallMetadata(request.Call, method))
        {
            return null;
        }

        var candidate = _candidates.GetOrAdd(method.CanonicalName, _ => Examine(method));
        if (candidate is null)
        {
            return null;
        }
        // The speed policy takes a forwarder's call as the call to the callee it forwards to (see ForwardTarget).
        if (!forwarded && _policy == ManagedMethodInliningPolicy.Speed && candidate.ForwardsTo is { } target)
        {
            return Resolve(request, target, forwarded: true);
        }
        // A wrapper that counts as small once the next round composes its priority callees into it does so only for a
        // call in the caller's own body (see IsSmall).
        var small = request.Nested ? candidate.Small : candidate.ComposesSmall;
        // The priority shapes are unbounded in size, so calls exposed by an
        // earlier round take only small and explicitly aggressive callees, and
        // not allocating ones: a lazily initialized getter inlined through its
        // allocating initializer put that cold path into every caller.
        if (request.SmallOnly && !(small && !candidate.Allocates) ||
            !(_policy == ManagedMethodInliningPolicy.Aggressive
                // Only what the callee asks for.
                ? candidate.Requested
                : _policy == ManagedMethodInliningPolicy.Loops
                // A field accessor inlines to the load the call's receiver
                // check already precedes, so it only shrinks its caller. So
                // does a small callee that receives an address, anywhere.
                ? candidate.Requested || candidate.Accessor || candidate.Always ||
                  small && candidate.ReceivesAnAddress && !(request.Nested && candidate.Allocates) ||
                  request.InLoop && (small && !(request.Nested && candidate.Allocates) ||
                                     candidate.Leaf || candidate.LoopBody)
                : small && !(request.Nested && candidate.Allocates) ||
                  (candidate.Leaf || candidate.LoopBody) && request.InLoop ||
                  (request.Nested
                      // A by-reference callee passed a local's address keeps the local in memory for the
                      // whole method, however deep the round that exposed the call.
                      ? candidate.Requested || candidate.TakesAnAddress && candidate.WithinByReferenceBudget &&
                        TakesPastTheBudget(request.LocalAddressArguments, request.InLoop, candidate.LoopLoadedParameters)
                      : candidate.Priority ||
                        candidate.TakesAnAddress &&
                        TakesPastTheBudget(request.LocalAddressArguments, request.InLoop, candidate.LoopLoadedParameters))) ||
            // A callvirt checks its receiver for null before the callee runs.
            !method.Definition.IsStatic && request.Call.Operation == CilOperation.CallVirtual &&
            !DereferencesReceiverFirst(candidate.Plan.Body))
        {
            return null;
        }
        return candidate.Plan;
    }

    // A call that triggers its type's initialization stays a call, except a static method of the caller's own type:
    // the caller runs only once that type's initialization was triggered, so the call's trigger finds it run or
    // running, as the JIT, which checks a class once, also finds it.
    private static bool TriggeredByCaller(MethodInstanceModel caller, MethodInstanceModel callee) =>
        callee.Definition.IsStatic && callee.DeclaringType.Equals(caller.DeclaringType) &&
        OwnTypeInitialization.InitializedOnEntry(caller.Definition, caller.DeclaringType.IsValueType);

    private Candidate? Examine(MethodInstanceModel method)
    {
        if (!_methodBodies.TryReadMethodBody(method, out var read))
        {
            return null;
        }

        var body = _specializer.Rewrite(read) with
        {
            MethodInstance = method,
        };
        if (!IsSupportedBody(method, body))
        {
            return null;
        }
        var accessor = !method.Definition.IsStatic && IsReferenceFieldAccessor(body);
        // .NET's JIT expands the core library's intrinsics in place. One this compiler leaves as a managed body asks to
        // be inlined, as AggressiveInlining does, when that body is small once specialized; a larger body is a
        // software fallback, which the JIT calls.
        var requested = method.Definition.IsAggressiveInlining || method.Definition.IsIntrinsic && CountsAsSmall(body);
        var priority = requested || (method.Definition.IsStatic ? IsPriorityCandidate(method, body, RemainingILBytes(read, body)) : accessor);
        // A value type's instance method receives its instance by address, as a by-reference static
        // method receives its argument, within the JIT's 100 bytes.
        var takesAnAddress = method.Definition.IsStatic
            ? TakesAnArgumentByReference(method)
            : method.DeclaringType.IsValueType && RemainingILBytes(read, body) <= ByReferenceBodyILBytes;
        var withinByReferenceBudget = RemainingILBytes(read, body) <= ByReferenceBodyILBytes;
        var stacks = new Lazy<ImmutableDictionary<int, ImmutableArray<CliValueKind>>>(() => _bodyValidator.Validate(body));
        var small = IsSmall(body, _smallBodyInstructionLimit, stacks);
        var composesSmall = _policy == ManagedMethodInliningPolicy.Speed
            ? IsSmall(body, ComposedBodyInstructionLimit, stacks)
            : small;
        var ilBytes = RemainingILBytes(read, body);
        var leaf = IsLeaf(body, ilBytes);
        var loopBody = !leaf && IsLoopBody(body);
        var allocates = Enumerable.Range(0, body.Instructions.Length).Any(index => Allocates(body, index));
        var always = !allocates && ilBytes <= AlwaysInlineILBytes &&
            !body.Instructions.Any(static instruction => instruction.Operation is CilOperation.Throw or CilOperation.Rethrow);
        var forwardsTo = requested || _policy != ManagedMethodInliningPolicy.Speed ? null : ForwardTarget(method, body);
        return priority || takesAnAddress || composesSmall || leaf || loopBody || forwardsTo is not null
            ? new(new(method, body) { InstructionEntryStacks = stacks.Value }, requested, forwardsTo, priority,
                accessor, small, composesSmall, leaf, loopBody, allocates, always,
                TakesAnArgumentByReference(method) || !method.Definition.IsStatic && method.DeclaringType.IsValueType,
                takesAnAddress, takesAnAddress ? LoopLoadedParameters(body) : [])
            {
                WithinByReferenceBudget = withinByReferenceBudget,
            }
            : null;
    }

    // The parameters of a static method that its body loads inside one of its own loops, by index. A caller's local
    // passes its address only to a by-reference parameter, through which each such load reads or writes the local
    // once for every time the loop repeats.
    private static ImmutableHashSet<int> LoopLoadedParameters(CilMethodBody body)
    {
        var loops = LoopSpans.Of(body);
        return
        [
            .. body.Instructions
                .Where(instruction => instruction.Operation == CilOperation.LoadArgument &&
                    LoopSpans.Contain(loops, instruction.Offset))
                .Select(static instruction => ((CilOperand.Index)instruction.Operand).Value),
        ];
    }

    // Small: the callee is small by itself, and calls no large priority callee (see IsSmall).
    // ComposesSmall: under the speed policy, the callee is small once the next round composes its large priority
    // callees into it; under the others, as Small.
    // Requested: the callee asks to be inlined, by AggressiveInlining or as a small intrinsic.
    // ForwardsTo: the callee asking to be inlined that this static callee only passes its arguments to, if any (see
    // ForwardTarget).
    // Always: the callee is within the JIT's always-inline size (ALWAYS_INLINE_SIZE, 16 IL bytes) and neither
    // allocates nor throws.
    // ReceivesAnAddress: the callee takes an argument by reference, or is a value type's instance method, which
    // receives its instance by address. A call that passes the address of one of the caller's locals keeps that
    // local in memory for the whole method, and every read and write of it a load and a store; once the callee is
    // inlined, the local is a plain one. .NET's JIT adds three to the benefit multiplier of a value type's method
    // (CALLEE_CLASS_VALUETYPE), as it does for one that is mostly loads and stores.
    // TakesAnAddress: a static callee that takes an argument by reference, a priority candidate of any size where a
    // call outside the caller's loops passes it the address of one of the caller's locals, or a call inside them passes
    // one in one of its LoopLoadedParameters (see ByReferenceBodyILBytes).
    private sealed record Candidate(
        ManagedInliningPlan Plan, bool Requested, MethodInstanceModel? ForwardsTo, bool Priority, bool Accessor, bool Small, bool ComposesSmall, bool Leaf,
        bool LoopBody, bool Allocates, bool Always, bool ReceivesAnAddress, bool TakesAnAddress,
        ImmutableHashSet<int> LoopLoadedParameters)
    {
        // Within the JIT's 100 bytes, so a later round may take it where a call passes a local's address.
        public bool WithinByReferenceBudget { get; init; }
    }

    // The IL bytes of a body as read whose code specialization left, as .NET's JIT measures a callee after ILLink has
    // removed the branches a feature switch decides: a branch a switch or constant decides leaves the code it cuts off
    // as nops, and that code does not count. Each instruction as read spans its IL bytes, up to the next instruction
    // in the decoder's numbering, which also ends an instruction the decoder expanded; in a body a switch's lowering
    // renumbered, up to the next source offset, the last, a return, a byte. Every instruction a rewrite makes from one
    // keeps its source offset, so that instruction counts once, and goes only when all of them are nops. A nop in the
    // IL counts, so a body nothing was removed from measures one past its furthest IL offset.
    private static int RemainingILBytes(CilMethodBody read, CilMethodBody body)
    {
        var spans = new Dictionary<int, int>();
        if (read.Instructions.Any(static instruction => instruction.OriginalOffset is null))
        {
            foreach (var instruction in read.Instructions)
            {
                spans[instruction.SourceOffset] = Math.Max(
                    spans.GetValueOrDefault(instruction.SourceOffset), instruction.NextOffset - instruction.SourceOffset);
            }
        }
        else
        {
            var starts = read.Instructions.Select(static instruction => instruction.SourceOffset).Distinct().Order().ToArray();
            for (var index = 0; index < starts.Length; index++)
            {
                spans[starts[index]] = index + 1 < starts.Length ? starts[index + 1] - starts[index] : 1;
            }
        }
        var remaining = body.Instructions
            .Where(static instruction => instruction.Operation != CilOperation.Nop)
            .Select(static instruction => instruction.SourceOffset)
            .Concat(read.Instructions
                .Where(static instruction => instruction.Operation == CilOperation.Nop)
                .Select(static instruction => instruction.SourceOffset))
            .ToHashSet();
        return spans.Where(span => remaining.Contains(span.Key)).Sum(static span => span.Value);
    }

    // A straight-line leaf makes no calls, not even to methods the compiler
    // replaces, since some of those allocate. It does not branch, allocate or
    // throw: without a branch a throw is unconditional, and inlining a throw
    // helper puts its cold path into every caller, as .NET's JIT avoids. It may
    // construct a value, as an operator returning a new struct does, when the
    // next round inlines the constructor: that only initializes storage.
    private bool IsLeaf(CilMethodBody body, int ilBytes) =>
        (InstructionCount(body) <= _leafBodyInstructionLimit ||
            ilBytes <= _leafBodyILBytes) &&
        Enumerable.Range(0, body.Instructions.Length).All(index =>
            body.Instructions[index] is var instruction &&
            (instruction.Operation is not (
                CilOperation.Call or CilOperation.CallVirtual or CilOperation.NewObject or CilOperation.Throw) ||
                instruction.Operation == CilOperation.NewObject && InlinedInTheNextRound(instruction)) &&
            !Allocates(body, index) &&
            instruction.Operand is not CilOperand.BranchTarget);

    // A loop body is larger than a leaf and may branch forward but not loop,
    // and neither allocates nor throws; each call it makes is to an aggressive
    // callee or to a small one that does not allocate, which the next round
    // inlines.
    private bool IsLoopBody(CilMethodBody body) =>
        InstructionCount(body) is var size &&
        size > _leafBodyInstructionLimit && size <= _loopBodyInstructionLimit &&
        body.Instructions.Count(static instruction => instruction.Operand is CilOperand.BranchTarget) <=
            LoopBodyBranchLimit &&
        Enumerable.Range(0, body.Instructions.Length).All(index =>
            body.Instructions[index] is var instruction &&
            instruction.Operation != CilOperation.Throw && !Allocates(body, index) &&
            (instruction.Operand is not CilOperand.BranchTarget target || target.Offset > instruction.Offset) &&
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
            _methodBodies.TryReadMethodBody(target, out var body) && CountsAsSmall(body) &&
            // A small intrinsic asks to be inlined, as an aggressive callee does.
            (definition.IsIntrinsic || !Enumerable.Range(0, body.Instructions.Length).Any(index => Allocates(body, index))));

    // The priority tier composes each large explicit priority callee into a
    // wrapper that calls it, so a wrapper is as large as its own instructions and
    // those callees' together, and is small while that stays within the limit.
    // Within the small limit, a wrapper of a large priority callee is never small.
    // Under the speed policy a call in the caller's own body takes a wrapper whose
    // whole stays within the loop-body tier's 56 instructions
    // (ComposedBodyInstructionLimit), since the next round composes its callees,
    // as the JIT, which judges a callee by its own IL, inlines a small argument
    // check that calls an aggressive comparison: ArgumentOutOfRangeException.
    // ThrowIfGreaterThanOrEqual<float> (12 instructions) calling float.CompareTo
    // (32) left Matrix4x4.CreatePerspective 1.4 times as slow as a call. Only an
    // aggressive callee composes: decimal.Round(decimal, int, MidpointRounding)
    // composed with its private by-reference overload was inlined into its
    // caller, where the overload, which asks for nothing, stayed a call. A call an
    // earlier round exposed does not: the rounds after the first take only what a
    // callee asks for, and the last ones only small callees, so the wrapper's
    // callees stay calls while its own code joins the inlined code around it.
    // A by-reference callee past the priority tier's budget stays a call in a
    // later round too, but under the speed policy the wrapper's own body takes
    // it where its call, outside the wrapper's loops, passes the address of one
    // of the wrapper's locals or arguments (see ByReferenceBodyILBytes), so such
    // a wrapper is not small either: inlined, it would leave in every caller a
    // call its own body composes, and the value whose address it passes in the
    // caller's memory. MemoryMarshal.TryGetArray (173 bytes of IL) called with
    // the address of its caller's out local stayed a call in a benchmark's
    // lambda, which then entered frames for the segment and for the callee:
    // 383 instructions a call, against 314 with the wrapper a call.
    // ValueStringBuilder.ToString (12 instructions around a 21-instruction
    // Span.Slice) inlined that way into a benchmark sink's formatting branch,
    // which never ran, and made every call of the sink 0.6 ns slower: the engine
    // saves the registers a function uses anywhere when it enters. The size
    // policies keep the small limit for the whole: composing there grew
    // Sdcb.SimdCodecs by 2% at Oz for no speed. A wrapper of a callee with no
    // body to read does not count as small. A small callee composes like any
    // other small callee in a later round, a by-reference callee past the tier's
    // budget stays a call, and a call that the compiler replaces with
    // instructions stays small.
    private bool IsSmall(
        CilMethodBody body,
        int limit,
        Lazy<ImmutableDictionary<int, ImmutableArray<CliValueKind>>> stacks)
    {
        if (!CountsAsSmall(body))
        {
            return false;
        }
        var composed = InstructionCount(body);
        StackSlotProducers? producers = null;
        for (var index = 0; index < body.Instructions.Length; index++)
        {
            var instruction = body.Instructions[index];
            if (instruction.Operation is not (CilOperation.Call or CilOperation.CallVirtual) ||
                _calledMethods.Resolve(instruction) is not { } target ||
                !(target.Definition.IsAggressiveInlining || TakesAnArgumentByReference(target)) ||
                _intrinsics.TryGetIntrinsic(target.Definition.Key, out _))
            {
                continue;
            }
            if (!target.Definition.HasManagedBody || !_methodBodies.TryReadMethodBody(target, out var callee))
            {
                return false;
            }
            if (CountsAsSmall(callee))
            {
                continue;
            }
            // Under the speed policy, whose priority tier takes such a callee, the budget is weighed on the callee as
            // specialized, as the tier weighs it (see Examine); the other policies keep the body as read.
            var specialized = new Lazy<CilMethodBody>(() => _specializer.Rewrite(callee));
            if (!IsExplicitPriorityCandidate(target, RemainingILBytes(callee, callee)) &&
                !(_policy == ManagedMethodInliningPolicy.Speed &&
                    IsExplicitPriorityCandidate(target, RemainingILBytes(callee, specialized.Value))))
            {
                if (_policy == ManagedMethodInliningPolicy.Speed && target.Definition.IsStatic &&
                    TakesPastTheBudget(
                        LocalAddressArguments.Of(body, index, target, stacks.Value, producers ??= new(body, stacks.Value)),
                        LoopSpans.Contain(LoopSpans.Of(body), instruction.Offset),
                        LoopLoadedParameters(specialized.Value)))
                {
                    return false;
                }
                continue;
            }
            // The next round takes only what a callee asks for, so a large by-reference callee that does not ask
            // stays a call there: inlined, the wrapper would leave it one in every caller instead of composing it
            // into its own body, and it is as large as that callee.
            if (!target.Definition.IsAggressiveInlining)
            {
                return false;
            }
            composed += InstructionCount(callee);
        }
        return composed <= limit;
    }

    private bool CountsAsSmall(CilMethodBody body) => InstructionCount(body) <= _smallBodyInstructionLimit;

    // The callee a static method's body only passes its arguments to, in order and unchanged, when that callee asks to
    // be inlined and has the same signature: `=> Callee(arguments)`, as float.SinCos is to MathF.SinCos. A call to the
    // forwarder is then the call to its callee, so the speed policy resolves it as that call. Inlined as a body of its
    // own, the forwarder only exposed its callee's call to the next round, a round deeper, where Matrix4x4's rotations
    // reached MathF.SinCos past the depth at which nested rounds take only small callees; kept a call, it returned
    // its pair through a value frame its caller allocated.
    private MethodInstanceModel? ForwardTarget(MethodInstanceModel method, CilMethodBody body)
    {
        if (!method.Definition.IsStatic)
        {
            return null;
        }
        var operations = body.Instructions.Where(static instruction => instruction.Operation != CilOperation.Nop).ToArray();
        var parameters = method.Signature.ParameterSignatureTypes;
        if (operations.Length != parameters.Length + 2 ||
            operations[^1].Operation != CilOperation.Return ||
            operations[^2].Operation != CilOperation.Call ||
            Enumerable.Range(0, parameters.Length).Any(index =>
                operations[index] is not { Operation: CilOperation.LoadArgument, Operand: CilOperand.Index { Value: var argument } } ||
                argument != index) ||
            _calledMethods.Resolve(operations[^2]) is not { Definition: { IsStatic: true, HasManagedBody: true } } target ||
            !target.Signature.ReturnSignatureType.Equals(method.Signature.ReturnSignatureType) ||
            !target.Signature.ParameterSignatureTypes.SequenceEqual(parameters) ||
            _intrinsics.TryGetIntrinsic(target.Definition.Key, out _) ||
            !_methodBodies.TryReadMethodBody(target, out var callee))
        {
            return null;
        }
        return target.Definition.IsAggressiveInlining ||
            target.Definition.IsIntrinsic && CountsAsSmall(_specializer.Rewrite(callee))
            ? target
            : null;
    }

    private static int InstructionCount(CilMethodBody body) =>
        body.Instructions.Count(static instruction => instruction.Operation != CilOperation.Nop);

    private bool Allocates(CilMethodBody body, int index) => body.Instructions[index].Operation switch
    {
        // Constructing a value type initializes storage in place, and an
        // exception constructed for a throw is the cold path. A supported
        // body ends in a return, so an instruction follows.
        CilOperation.NewObject => _calledMethods.Resolve(body.Instructions[index]) is not { DeclaringType.IsValueType: true } &&
            body.Instructions[index + 1].Operation != CilOperation.Throw,
        CilOperation.NewArray or CilOperation.NewRectangularArray or CilOperation.Box => true,
        // Specialization turns a string's newobj into a call to its static Ctor, which still allocates the string:
        // read as a call, Span<char>.ToString counted as not allocating, so a nested round inlined it.
        CilOperation.Call => StringConstructorRewriter.IsConstructorCall(_calledMethods.Resolve(body.Instructions[index])),
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
                    _methodBodies.TryReadMethodBody(target, out var targetBody) &&
                    DereferencesReceiverFirst(targetBody, depth + 1)),
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
        CilMethodBody body,
        int ilBytes) =>
        IsExplicitPriorityCandidate(method, ilBytes) ||
        IsAggregateAddressForwardingWrapper(method, body);

    private static bool IsExplicitPriorityCandidate(MethodInstanceModel method, int ilBytes) =>
        method.Definition.IsAggressiveInlining ||
        TakesAnArgumentByReference(method) && ilBytes <= ByReferenceBodyILBytes;

    // Past its budget the priority tier takes a static by-reference callee only where the call, outside the caller's
    // loops, passes the address of one of the caller's locals or arguments, or, inside them, passes one in a parameter
    // the callee loads in a loop of its own (see ByReferenceBodyILBytes).
    private static bool TakesPastTheBudget(
        ImmutableHashSet<int> localAddressArguments,
        bool inLoop,
        ImmutableHashSet<int> loopLoadedParameters) =>
        !localAddressArguments.IsEmpty && !inLoop || localAddressArguments.Overlaps(loopLoadedParameters);

    private static bool TakesAnArgumentByReference(MethodInstanceModel method) =>
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
