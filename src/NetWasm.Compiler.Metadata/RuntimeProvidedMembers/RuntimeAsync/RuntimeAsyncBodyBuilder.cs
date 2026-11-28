// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.Core;
using static NetWasm.Compiler.Metadata.RuntimeProvidedMembers.CilBodyElements;
using static NetWasm.Compiler.Metadata.RuntimeProvidedMembers.RuntimeAsync.RuntimeAsyncAwaits;

namespace NetWasm.Compiler.Metadata.RuntimeProvidedMembers.RuntimeAsync;

// Lowers a runtime-async method (MethodImplOptions.Async, from Roslyn's runtime-async=on) onto CoreLib's RuntimeAsync.
// Upstream's JIT does this transformation (dotnet/runtime v11.0.0, src/coreclr/jit/async.cpp), suspending into a
// Continuation per suspension and resuming through its ResumeInfo; NetWasm rewrites the CIL instead, onto a frame per
// activation that resumes through a delegate to the method, with the semantics of upstream's AsyncHelpers.CoreCLR.cs.
// The method keeps its signature and runs as:
//   mode = TakeCallMode(); contexts = CaptureContexts()
//   try {
//       if (mode == Resumed) { frame = TakeResumingFrame(); restore the arguments and locals; dispatch on its state }
//       the body, where each AsyncHelpers await becomes: if not completed { save into the activation's frame (made at
//       its first suspension, then reused); subscribe; push the frame; leave to the suspension exit } resume_k: take
//       back what was saved; continue; and each ret leaves to the completion exit. An await
//       of the task a statically bound call of a lowered method has just returned calls that method directly, as
//       desktop's JIT does: CallDirect(); call; if the callee suspended { save and push the frame; leave } resume_k:
//       TakeResult(), with no task made for the call and the callee's exception thrown at the await
//   } catch (Exception e) { RestoreContexts; a Normal call returns a faulted task; the others rethrow }
//   completion exit: RestoreContexts; Complete(result, mode)
//   suspension exit: returned = Suspended(mode); RestoreContextsOnSuspension; returned
// A call that is not a resumption so leaves its caller's ExecutionContext and SynchronizationContext as it found them,
// as upstream's runtime async does (CoreLib's RuntimeAsync carries each frame's context across the suspension). Each
// capture and restore is guarded by AmbientContextChanges.Made, which only a program that changes a context writes, so
// in every other program they fold away with the guard.
// The dispatch reaches a resume point through the head of each loop (where control enters it, CilLoopHeads) and the
// start of each protected block that contains it, outermost first, so the control flow stays reducible and a protected
// block is entered at its start; a suspension leaves its protected blocks without running their finally handlers. The
// values under an awaited one on the evaluation stack are spilled to locals of their types across it. A suspension
// keeps only the arguments and locals live after it (CilLiveness), restored where it resumes, each in a field of its own
// type in the frame, which the method's suspensions share by type (FrameLayout); a slot that can hold a reference is
// cleared once read. Awaits inside a handler, and a reference or an untyped value on the stack across an await are
// named (NW1001) rather than lowered.
internal sealed class RuntimeAsyncBodyBuilder(Func<IRuntimeAsyncMemberResolver> members,
    Func<IRuntimeAsyncFrameLayoutResolver> frames, ITypeDefinitionResolver definitions, ICilBodyLayout layout,
    ISymbolFormatter symbols, IMethodRepository methods, IFieldRepository fields, IMetadataStackTypeResolver stackTypes,
    IRuntimeAsyncSuspensionFacts suspensions, IRuntimeAsyncActivationGuardSelector guards,
    IRuntimeAsyncDirectSiteEmitter directSites)
    : IRuntimeAsyncBodyBuilder
{
    private const int ResumedMode = 2;
    private static readonly CliTypeIdentity NativeInt = CliTypeIdentity.Primitive("nativeint", CliValueKind.NativeInt);

    private readonly Lazy<IRuntimeAsyncMemberResolver> _members = new(members);
    private readonly Lazy<IRuntimeAsyncFrameLayoutResolver> _frames = new(frames);

    // An await: the values under the awaited one on the evaluation stack (bottom first) are spilled across it.
    // An await; Direct is the lowered method whose call the await follows at once, which the caller then calls directly:
    // its result comes through the result slot, and no Task is made for the call.
    private sealed record Site(int Number, int Offset, MethodInstanceModel Await, CliTypeIdentity Awaited, bool IsAwaiter,
        ImmutableArray<CliTypeIdentity> SpillTypes)
    {
        public int? DirectCallOffset { get; init; }
        // A direct await of a callee that passes its reference result as the call's value (ReturnsResult).
        public bool TakesReturned { get; init; }
        // An await fused with its ConfigureAwait (a direct call's, a virtual call's or a leaf's): the constant and the
        // ConfigureAwait call (and a value task's local store and address), which the await drops, whether the
        // constant is false (the frame or subscription then continues without its SynchronizationContext), and the
        // task type, which the await then finds on the stack.
        public ImmutableArray<int> DroppedOffsets { get; init; } = [];
        // A virtual or interface call whose task the await takes at once: the call asks whichever override runs to take
        // it directly (CallDirectVirtual); the await then takes the direct path or the task's (VirtualDone).
        public int? VirtualCallOffset { get; init; }
        public bool ContinuesAnywhere { get; init; }
        public CliTypeIdentity? FusedTask { get; init; }
        // What a suspension here keeps: the arguments and locals live after it, and the await's own values.
        public ImmutableArray<SavedValue> Saved { get; init; } = [];

        public CilBodyLocal Temporary { get; init; } = null!;
        public ImmutableArray<CilBodyLocal> Spills { get; init; } = [];
        public CilBodyLabel Resume { get; } = new();
    }

    // Where the dispatch passes on its way to a resume point: a loop or a protected block, the offsets it covers, and
    // where the dispatch enters it (Head): a loop's head, or a protected block's start. A protected block is entered
    // only by falling into it, so the dispatch reaches it through an entry just before it.
    private sealed record Point(int Start, int End, bool IsProtected, int Head)
    {
        public CilBodyLabel Body { get; } = new();

        public CilBodyLabel Entry { get; } = new();
    }

    public CilMethodBody Build(MethodInstanceModel method, CilMethodBody body)
    {
        ArgumentNullException.ThrowIfNull(method);
        ArgumentNullException.ThrowIfNull(body);
        var runtime = _members.Value;
        // The runtime-async method lowered: the method itself, or the method whose separate body this is
        // (RuntimeAsyncBodies), which takes the method's parameters and then the call mode.
        var lowered = method.Definition.RuntimeAsyncBodyOf is null ? method : RuntimeAsyncBodies.Of(methods, method);
        var returnType = method.Signature.ReturnSignatureType;
        var (shape, result) = Shape(returnType) ?? throw Unsupported(lowered, "returns neither a Task nor a ValueTask");
        // The result type as a stack type: an enum from another assembly is its underlying integer, as the value the
        // method returns is, where the return type's argument names it as a value type.
        if (result is not null) result = stackTypes.Resolve(result);
        var parameters = lowered.Signature.ParameterSignatureTypes;
        if (parameters.Any(parameter => parameter.Shape == CliTypeShape.ManagedByReference))
            throw Unsupported(lowered, "takes a parameter by reference");
        // A frame resumes through a delegate over the method, or over its resumption stub when the method takes more
        // parameters than a delegate does (RuntimeAsyncResumers); the stub's frame takes no parameters.
        var resumes = RuntimeAsyncResumers.Needs(lowered.Signature) ? RuntimeAsyncResumers.For(methods, lowered) : lowered;
        var stacks = CilStackTypes.Compute(lowered, body, fields) ??
            throw Unsupported(lowered, "has an instruction the runtime async lowering does not follow");
        var sites = Sites(lowered, body, runtime, stacks);
        // A direct await of a method that cannot suspend (IRuntimeAsyncSuspensionFacts) completes at once: it takes the
        // callee's result as the call returns and saves nothing, so it is no suspension point. The awaits that can
        // suspend are numbered from 1; a method with none is never resumed and has no frame.
        var completing = new Dictionary<int, Site>();
        if (sites.Any(site => site.DirectCallOffset is not null))
        {
            var calls = new Dictionary<int, CilInstruction>(body.Instructions.Length);
            foreach (var instruction in body.Instructions) calls[instruction.Offset] = instruction;
            foreach (var site in sites)
            {
                if (site.DirectCallOffset is { } call &&
                    calls[call].Operand is CilOperand.MethodInstance { Value: var callee } && !suspensions.CanSuspend(callee))
                {
                    completing.Add(site.Offset, site with { TakesReturned = ReturnsResult(callee) });
                }
            }
        }
        if (completing.Count != 0)
        {
            sites = [.. sites.Where(site => !completing.ContainsKey(site.Offset))
                .Select((site, index) => site with { Number = index + 1 })];
        }
        var neverSuspends = sites.IsEmpty;
        var outerPoints = Points(body, sites);
        var nestedRuns = NestedRuns(body, sites, outerPoints);
        ImmutableArray<Point> loops =
            [.. outerPoints, .. nestedRuns.Values.SelectMany(runs => runs).Select(run => run.Point).OfType<Point>()];

        // Only a virtual or interface method can be the override a virtual call's request reaches (DirectVirtual), so only
        // such a method takes its call mode, and completes, through the helpers that handle one.
        var overrides = lowered.Definition.IsVirtual;
        var reading = CilBodyDrafts.Read(body);
        var labels = reading.Labels;
        var mode = new CilBodyLocal(runtime.Resolve(Helper("TakeCallMode")).Signature.ReturnSignatureType);
        // A method written as runtime async, called directly (Direct or DirectVirtual, the odd modes), gives its
        // exception to its caller as desktop's does, with no handler of its own in the first pass: its catch, which
        // then only puts the caller's state back and rethrows, is passed by while `direct` is set. A rewritten state
        // machine's catch stands for the one its MoveNext had, so it stays a handler, as desktop's machine is one. An
        // async iterator's MoveNextAsync keeps its MoveNext's own catch, which is the handler, so its outer one is passed
        // by too, unless the program changes an ambient context: the caller's filters then run after its restore, as
        // they do after desktop's MoveNext has returned.
        var iterator = lowered.Definition is { AsyncIteratorMoveNext: not null } or { AsyncIteratorDisposeOf: not null };
        var passThrough = iterator ||
            lowered.Definition is { StateMachineMoveNext: null, AsyncIteratorMoveNext: null, AsyncIteratorDisposeOf: null };
        var direct = new CilBodyLocal(CliTypeIdentity.FromStackKind(CliValueKind.I4));
        var captured = new CilBodyLabel();
        var contextsChanged = new CilOperand.FieldInstance(runtime.ContextsChanged);
        // Skips to the label unless the program has changed an ambient context.
        IEnumerable<CilBodyElement> ContextsChanged(CilBodyLabel skip) =>
            [Op(CilOperation.LoadStaticField, contextsChanged), Branch(CilOperation.BranchIfFalse, skip)];
        // The contexts the exits restore, in three locals (RuntimeAsync.CaptureExecution): the method enters no value
        // frame for them.
        var execution = new CilBodyLocal(runtime.Resolve(Helper("CaptureExecution")).Signature.ReturnSignatureType);
        var token = new CilBodyLocal(CliTypeIdentity.FromStackKind(CliValueKind.I4));
        var synchronization = new CilBodyLocal(runtime.Resolve(Helper("CaptureSynchronization")).Signature.ReturnSignatureType);
        var returned = new CilBodyLocal(returnType);
        var state = new CilBodyLocal(CliTypeIdentity.FromStackKind(CliValueKind.I4));
        var suspending = new CilBodyLocal(CliTypeIdentity.FromStackKind(CliValueKind.I4));
        var exception = new CilBodyLocal(CliTypeIdentity.FromDefinition(runtime.Exception));
        var faulted = new CilBodyLocal(returnType);
        var resultLocal = result is null ? null : new CilBodyLocal(result);
        // A site's locals hold its values only from its await's start until it resumes, and awaits do not nest, so the
        // sites share them by type: the k-th local of a type one site takes is the k-th of every other's. A local per
        // site made each method's locals grow with its awaits, and passes that keep every local's state at each
        // instruction grew with their square.
        var shared = new Dictionary<(CliTypeIdentity Type, int Ordinal), CilBodyLocal>();
        var siteLocals = new List<CilBodyLocal>();
        var taken = new Dictionary<CliTypeIdentity, int>();
        CilBodyLocal Take(CliTypeIdentity type)
        {
            var ordinal = taken.GetValueOrDefault(type);
            taken[type] = ordinal + 1;
            if (!shared.TryGetValue((type, ordinal), out var local))
            {
                local = new CilBodyLocal(type);
                shared.Add((type, ordinal), local);
                siteLocals.Add(local);
            }
            return local;
        }
        sites = [.. sites.Select(site =>
        {
            taken.Clear();
            return site with
            {
                Temporary = Take(site.FusedTask ?? site.Awaited),
                Spills = [.. site.SpillTypes.Select(Take)],
            };
        })];
        var firstArgument = method.Definition.IsStatic ? 0 : 1;
        var liveness = CilLiveness.Compute(body, firstArgument + parameters.Length,
            CilAddressUses.Compute(body, firstArgument + parameters.Length, IsByRefLike),
            [.. sites.Select(site => site.Offset)]);
        var saveable = reading.Draft.Locals
            .Select((local, index) => (Local: local, Index: index))
            .Where(pair => !pair.Local.Pinned && pair.Local.Type.Shape != CliTypeShape.ManagedByReference &&
                !IsByRefLike(pair.Local.Type))
            .ToDictionary(pair => pair.Index);
        // Each await's live values, in index order: from its live sets, not a test of every variable at every await.
        sites = [.. sites.Select(site =>
        {
            var (liveLocals, liveArguments) = liveness.LiveAfter(site.Offset);
            return (Site: site, Locals: liveLocals, Arguments: liveArguments);
        }).Select(live => live.Site with
        {
            Saved =
            [
                .. live.Arguments.Where(argument => argument >= firstArgument)
                    .Select(argument => new SavedValue(parameters[argument - firstArgument], argument, null)),
                .. live.Locals.Where(saveable.ContainsKey)
                    .Select(local => new SavedValue(saveable[local].Local.Type, null, saveable[local].Local)),
                // A leaf await reads its awaited value again once it resumes, to take the result.
                .. live.Site.DirectCallOffset is null && !live.Site.IsAwaiter
                    ? [new SavedValue(live.Site.Temporary.Type, null, live.Site.Temporary)]
                    : Array.Empty<SavedValue>(),
                .. live.Site.Spills.Select(spill => new SavedValue(spill.Type, null, spill)),
            ],
        })];

        // Every suspension of an activation saves into one frame, typed for what its suspensions keep.
        var frames = neverSuspends
            ? null
            : Frame(_frames.Value, sites, ReferenceEquals(resumes, lowered) ? parameters : [], returnType);
        var frame = frames is null ? null : new CilBodyLocal(frames.Type);

        var tryStart = new CilBodyLabel();
        var handler = new CilBodyLabel();
        var handlerEnd = new CilBodyLabel();
        var completionExit = new CilBodyLabel();
        var suspensionExit = new CilBodyLabel();
        var faultExit = new CilBodyLabel();
        var start = labels[body.Instructions[0].Offset];
        var siteAt = sites.ToDictionary(site => site.Offset);
        var directCalls = sites.Where(site => site.DirectCallOffset is not null)
            .Select(site => site.DirectCallOffset!.Value)
            .ToHashSet();
        var dropped = sites.SelectMany(site => site.DroppedOffsets).ToHashSet();
        // A completing await keeps its direct call, and drops what configured the call's task, as any direct await.
        directCalls.UnionWith(completing.Values.Select(site => site.DirectCallOffset!.Value));
        dropped.UnionWith(completing.Values.SelectMany(site => site.DroppedOffsets));
        var virtualCalls = sites.Where(site => site.VirtualCallOffset is not null)
            .Select(site => site.VirtualCallOffset!.Value)
            .ToHashSet();
        var loopAt = outerPoints.ToDictionary(loop => loop.Head);
        // A branch back to where nested protected blocks start comes from inside the innermost of them (the only
        // way in from outside is a leave, to the outermost start), so it goes to the innermost one's start.
        var innermostStarts = nestedRuns.ToDictionary(pair => labels[pair.Key],
            pair => (Label: pair.Value[^1].Start,
                End: pair.Key + pair.Value[^1].Regions.Min(index => body.ExceptionRegions[index].TryLength)));
        var runStarts = nestedRuns.Values.SelectMany(runs => runs)
            .SelectMany(run => run.Regions.Select(region => (Region: region, run.Start)))
            .ToDictionary(pair => pair.Region, pair => pair.Start);
        if (loopAt.TryGetValue(body.Instructions[0].Offset, out var first) && first.IsProtected) start = first.Entry;
        // A body that opens with a protected block (SemaphoreSlim.WaitUntilCountOrTimeoutAsync's try around its first
        // call) is entered at that block's start by falling into it: the dispatch, inside the lowering's own protected
        // block, branches to a nop before it rather than into it.
        CilBodyLabel? bodyEntry = null;
        if (start == labels[body.Instructions[0].Offset] &&
            body.ExceptionRegions.Any(region => region.TryOffset == body.Instructions[0].Offset))
        {
            bodyEntry = new CilBodyLabel();
            start = bodyEntry;
        }
        var siteOffsets = SiteOffsets(sites);
        var guarded = body.ExceptionRegions
            .Where(region => region.Kind == CilExceptionRegionKind.Finally &&
                HasSiteIn(siteOffsets, region.TryOffset, region.TryOffset + region.TryLength))
            .Select(region => region.HandlerOffset)
            .ToHashSet();

        // The filters and handlers of each protected block that holds a direct call, where a request the callee never took
        // (the call threw first) is dropped: they run before any other code once such a call throws, the method's own
        // catch-all included.
        var directOffsets = directCalls.Concat(virtualCalls).Order().ToArray();
        var clearsDirect = body.ExceptionRegions
            .Where(region => HasSiteIn(directOffsets, region.TryOffset, region.TryOffset + region.TryLength))
            .SelectMany(region => region.FilterOffset is { } filter
                ? new[] { region.HandlerOffset, filter }
                : new[] { region.HandlerOffset })
            .ToHashSet();
        var (chains, held) = Enclosings(sites, loops);
        var pointIndex = new Dictionary<Point, int>(ReferenceEqualityComparer.Instance);
        for (var index = 0; index < loops.Length; index++) pointIndex[loops[index]] = index;
        // Where the dispatch goes for the await from a point: the next point in, or the await's resume point.
        CilBodyLabel Next(int site, int position)
        {
            var chain = chains[site];
            if (position == chain.Count) return sites[site].Resume;
            var point = loops[chain[position]];
            return point.IsProtected ? point.Entry : labels[point.Head];
        }
        // The dispatch at a point: its own awaits, each to where it goes from there.
        IEnumerable<CilBodyElement> DispatchAt(Point point) =>
            Dispatch(state, [.. held[pointIndex[point]].Select(entry => (sites[entry.Site].Number, Next(entry.Site, entry.Position + 1)))],
                point.Body);

        var guard = guards.Select(method).Guard(new RuntimeAsyncGuardRequest(
            lowered,
            method,
            mode,
            direct,
            passThrough,
            iterator,
            contextsChanged,
            tryStart,
            runtime.Exception.Key,
            CliTypeIdentity.FromDefinition(runtime.Exception),
            new RuntimeAsyncCatchShape(
                handler,
                handlerEnd,
                exception,
                mode,
                directOffsets.Length != 0,
                [.. RestoreContexts("RestoreContexts")],
                null,
                neverSuspends ? null : suspensionExit,
                Faulted(runtime, shape, result),
                faulted,
                faultExit)));
        List<CilBodyElement> elements =
        [
            .. frame is null ? [] : new[] { Op(CilOperation.LoadNull), Store(frame) },
            Op(CilOperation.LoadInt32, new CilOperand.ConstantI4(0)),
            Store(state),
            Op(CilOperation.LoadInt32, new CilOperand.ConstantI4(0)),
            Store(suspending),
            .. guard.TakesMode,
            .. ContextsChanged(captured),
            Call(runtime.Resolve(Helper("CaptureExecution"))),
            Store(execution),
            Call(runtime.Resolve(Helper("CapturedToken"))),
            Store(token),
            Call(runtime.Resolve(Helper("CaptureSynchronization"))),
            Store(synchronization),
            // The guard's target stays outside the protected block, which is entered at its start.
            Mark(captured),
            Op(CilOperation.Nop),
            Mark(tryStart),
        ];
        if (frames is null || frame is null)
        {
            // Never resumed: the body starts here.
            elements.Add(Branch(CilOperation.Branch, start));
        }
        else
        {
            elements.AddRange([
                Load(mode),
                Op(CilOperation.LoadInt32, new CilOperand.ConstantI4(ResumedMode)),
                Branch(CilOperation.BranchIfNotEqual, start),
                Call(runtime.Resolve(new RuntimeAsyncMember.Named("TakeResumingFrame", [frames.Type]))),
                Store(frame),
                Load(frame),
                Call(runtime.Resolve(Helper("State"))),
                Store(state),
                .. Dispatch(state, [.. sites.Select((site, index) => (site.Number, Next(index, 0)))], start),
            ]);
        }

        var labelOffsets = new Dictionary<CilBodyLabel, int>(ReferenceEqualityComparer.Instance);
        foreach (var (labelOffset, label) in labels) labelOffsets.Add(label, labelOffset);
        var offset = -1;
        foreach (var element in reading.Draft.Elements)
        {
            if (element is CilBodyElement.Mark mark)
            {
                offset = labelOffsets[mark.Label];
                if (loopAt.TryGetValue(offset, out var entered) && entered.IsProtected)
                    elements.AddRange([Mark(entered.Entry), Op(CilOperation.Nop)]);
                if (bodyEntry is not null && offset == body.Instructions[0].Offset)
                {
                    elements.AddRange([Mark(bodyEntry), Op(CilOperation.Nop)]);
                    bodyEntry = null;
                }
                elements.Add(mark);
                if (clearsDirect.Contains(offset))
                {
                    // A direct call in the protected block may have thrown before its callee took the request.
                    elements.Add(Call(runtime.Resolve(Helper("ClearDirect"))));
                }
                if (guarded.Contains(offset))
                {
                    // A finally handler that a suspension leaves through runs only on the way out of a completed block.
                    var handlerBody = new CilBodyLabel();
                    elements.AddRange([
                        Load(suspending),
                        Branch(CilOperation.BranchIfFalse, handlerBody),
                        Op(CilOperation.EndFinally),
                        Mark(handlerBody),
                    ]);
                }
                if (loopAt.TryGetValue(offset, out var loop))
                {
                    elements.AddRange([
                        Load(state),
                        Branch(CilOperation.BranchIfFalse, loop.Body),
                        .. DispatchAt(loop),
                        Mark(loop.Body),
                    ]);
                }
                if (nestedRuns.TryGetValue(offset, out var runs))
                {
                    foreach (var run in runs)
                    {
                        // A label names the next instruction, so the enclosing dispatch's end, marked just before,
                        // names this nop rather than the nested block's first instruction.
                        if (run.Point is { } inner) elements.Add(Mark(inner.Entry));
                        elements.AddRange([Op(CilOperation.Nop), Mark(run.Start)]);
                        if (run.Point is { } point)
                        {
                            elements.AddRange([
                                Load(state),
                                Branch(CilOperation.BranchIfFalse, point.Body),
                                .. DispatchAt(point),
                                Mark(point.Body),
                            ]);
                        }
                    }
                }
                continue;
            }
            if (dropped.Contains(offset))
            {
                continue;
            }
            if (virtualCalls.Contains(offset))
            {
                // Whichever override runs takes the call directly if it is lowered; any other declines it.
                elements.AddRange([Call(runtime.Resolve(Helper("CallDirectVirtual"))), element]);
                continue;
            }
            if (directCalls.Contains(offset) &&
                element is CilBodyElement.Instruction { Operand: CilOperand.MethodInstance { Value: var callee } } call)
            {
                elements.AddRange(directSites.Emit(new RuntimeAsyncDirectSite(callee, call)));
                continue;
            }
            if (completing.TryGetValue(offset, out var completed))
            {
                elements.AddRange(Completed(completed, runtime, element.Source));
                continue;
            }
            if (siteAt.TryGetValue(offset, out var site))
            {
                elements.AddRange(Await(site, resumes, frames!, frame!, state, suspending, suspensionExit, runtime,
                    element.Source));
                continue;
            }
            if (element is CilBodyElement.Instruction { Operation: CilOperation.Return })
            {
                if (resultLocal is not null) elements.Add(Store(resultLocal) with { Source = element.Source });
                elements.Add(Branch(CilOperation.Leave, completionExit) with { Source = element.Source });
                continue;
            }
            elements.Add(element switch
            {
                CilBodyElement.Branch branch when Innermost(branch.Target) is { } target =>
                    branch with { Target = target },
                CilBodyElement.Switch branches when branches.Targets.Any(target => Innermost(target) is not null) =>
                    branches with { Targets = [.. branches.Targets.Select(target => Innermost(target) ?? target)] },
                _ => element,
            });
        }

        CilBodyLabel? Innermost(CilBodyLabel target) =>
            innermostStarts.TryGetValue(target, out var innermost) && offset >= labelOffsets[target] &&
            offset < innermost.End
                ? innermost.Label
                : null;

        IEnumerable<CilBodyElement> RestoreContexts(string helper)
        {
            var restored = new CilBodyLabel();
            return [.. ContextsChanged(restored), Load(mode), Load(execution), Load(token), Load(synchronization),
                Call(runtime.Resolve(Helper(helper))), Mark(restored)];
        }

        elements.AddRange([
            .. guard.Catch,
            Mark(completionExit),
            .. RestoreContexts("RestoreContexts"),
        ]);
        if (resultLocal is not null) elements.Add(Load(resultLocal));
        elements.AddRange([
            Load(mode),
            Call(ReturnsResult(lowered)
                ? runtime.Resolve(new RuntimeAsyncMember.Named("CompleteReturning", [result!]))
                : Completion(runtime, overrides ? "VirtualComplete" : "Complete", shape, result)),
            Op(CilOperation.Return),
        ]);
        if (!neverSuspends)
        {
            elements.AddRange([
                Mark(suspensionExit),
                Load(mode),
                Call(Completion(runtime, overrides ? "VirtualSuspended" : "Suspended", shape, result)),
                Store(returned),
                .. RestoreContexts("RestoreContextsOnSuspension"),
                Load(returned),
                Op(CilOperation.Return),
            ]);
        }
        elements.AddRange(guard.FaultExit);

        var draft = new CilBodyDraft(
            [.. reading.Draft.Locals, mode, execution, token, synchronization, returned, .. frame is null ? [] : new[] { frame },
                state, suspending, exception, faulted, .. resultLocal is null ? [] : new[] { resultLocal },
                .. guard.UsesDirect ? new[] { direct } : [],
                .. siteLocals],
            [.. elements],
            [.. reading.Draft.Regions.Select((region, index) => runStarts.TryGetValue(index, out var runStart)
                    ? region with { TryStart = runStart }
                    : region),
                .. guard.Region is { } guardRegion ? [guardRegion] : Array.Empty<CilBodyRegion>()])
        {
            InitializesLocals = reading.Draft.InitializesLocals,
        };
        return layout.Lay(draft, method, body.MaxStack + 6);
    }

    // Whether a lowered method passes its result to a direct caller as the call's value (RuntimeAsync.CompleteReturning):
    // a non-virtual method that cannot suspend (so a call of it is Normal or Direct, and every direct await of it takes
    // the Completed path) whose Task's result is a reference. Its callers ask the same of it, so the two agree.
    private bool ReturnsResult(MethodInstanceModel method) =>
        !method.Definition.IsVirtual && !suspensions.CanSuspend(method) &&
        Shape(method.Signature.ReturnSignatureType) is (ResultShape.TaskOfResult, { IsValueType: false });

    // A direct await of a method that cannot suspend: the call left its result in the slot (a direct call returns no task),
    // so the await drops what the call returned and takes the result, or the callee's exception, at once; a callee that
    // returns its reference result as the call's value hands it to TakeReturned instead.
    private static IEnumerable<CilBodyElement> Completed(Site site, IRuntimeAsyncMemberResolver runtime, int? source)
    {
        var result = site.Await.Signature.ReturnSignatureType;
        if (site.TakesReturned)
        {
            return [Call(runtime.Resolve(new RuntimeAsyncMember.Named("TakeReturned", [result]))) with { Source = source }];
        }
        return
        [
            Op(CilOperation.Pop) with { Source = source },
            Call(result.StackKind == CliValueKind.Void
                ? runtime.Resolve(Helper("TakeVoid"))
                : runtime.Resolve(new RuntimeAsyncMember.Named("TakeResult", [result]))),
        ];
    }

    // An await: if it cannot complete at once, save the frame, subscribe to what it awaits, push the frame and leave to
    // the suspension exit; resumption continues where a completed await would.
    // `resumes`: what the frame's delegate calls, the method or its resumption stub.
    private static List<CilBodyElement> Await(Site site, MethodInstanceModel resumes, FrameLayout frames,
        CilBodyLocal frame, CilBodyLocal state, CilBodyLocal suspending, CilBodyLabel suspensionExit,
        IRuntimeAsyncMemberResolver runtime, int? source)
    {
        var done = new CilBodyLabel();
        var frameConstructor = runtime.Resolve(new RuntimeAsyncMember.Constructor(frames.Type));
        var direct = site.DirectCallOffset is not null;
        var viaVirtual = site.VirtualCallOffset is not null;
        var task = site.Temporary.Type;
        var slots = frames.Sites[site.Number - 1];
        var sequence = new List<CilBodyElement> { Store(site.Temporary) with { Source = source } };
        sequence.AddRange(site.Spills.Reverse().Select(spill => Store(spill)));
        if (direct)
        {
            // A direct callee returns no task; if it suspended, its frames are pushed and the caller follows them.
            sequence.AddRange([
                Call(runtime.Resolve(Helper("IsSuspending"))),
                Branch(CilOperation.BranchIfFalse, done),
            ]);
        }
        else if (viaVirtual)
        {
            // Done when the override that took the call completed, or when the task of one that did not has.
            sequence.AddRange([
                Address(site.Temporary),
                Call(runtime.Resolve(new RuntimeAsyncMember.ForAwaited("VirtualDone", task))),
                Branch(CilOperation.BranchIfTrue, done),
            ]);
        }
        else if (!site.IsAwaiter)
        {
            sequence.AddRange([
                .. ReadAwaited(site, "IsCompleted", runtime),
                Branch(CilOperation.BranchIfTrue, done),
            ]);
        }
        // The activation's frame, made at its first suspension (with a delegate to the method, which resuming calls) and
        // reused at every later one, a resumption included: it comes back as the frame this activation resumes from.
        var reuse = new CilBodyLabel();
        sequence.AddRange([
            Load(frame),
            Branch(CilOperation.BranchIfTrue, reuse),
            .. ResumerTarget(resumes),
            Op(CilOperation.LoadFunction, new CilOperand.MethodInstance(resumes)),
            Op(CilOperation.NewObject, new CilOperand.MethodInstance(
                runtime.Resolve(new RuntimeAsyncMember.Constructor(frameConstructor.Signature.ParameterSignatureTypes[0])))),
            Op(CilOperation.NewObject, new CilOperand.MethodInstance(frameConstructor)),
            Store(frame),
            Mark(reuse),
        ]);
        for (var index = 0; index < site.Saved.Length; index++)
        {
            var saved = site.Saved[index];
            var path = frames.Paths[slots[index]];
            sequence.AddRange([
                .. Owner(frame, path),
                saved.Argument is { } argument ? Argument(argument) : Load(saved.Local!),
                Op(CilOperation.StoreField, new CilOperand.FieldInstance(path[^1])),
            ]);
        }
        // One call suspends: the frame's state and contexts, the subscription of a leaf await, and the frame pushed. A
        // value task's subscription reads it through its address, as its other reads do (ReadAwaited); a task awaited
        // through ConfigureAwait(false) subscribes to continue wherever it completes.
        var leafValueTask = !direct && !viaVirtual && !site.IsAwaiter && IsValueTask(task);
        var leafSuspension = (leafValueTask ? "SuspendOnAt" : "SuspendOn") + (site.ContinuesAnywhere ? "Anywhere" : "");
        sequence.AddRange([
            .. direct ? Array.Empty<CilBodyElement>() : [leafValueTask ? Address(site.Temporary) : Load(site.Temporary)],
            Load(frame),
            Op(CilOperation.LoadInt32, new CilOperand.ConstantI4(site.Number)),
            Call(direct
                ? runtime.Resolve(Helper(site.ContinuesAnywhere ? "SuspendDirectAnywhere" : "SuspendDirect"))
                : viaVirtual
                ? runtime.Resolve(new RuntimeAsyncMember.ForAwaited(
                    site.ContinuesAnywhere ? "SuspendVirtualAnywhere" : "SuspendVirtual", task))
                : site.IsAwaiter
                    ? runtime.Resolve(new RuntimeAsyncMember.Named(
                        site.Await.Definition.Name == "UnsafeAwaitAwaiter" ? "SuspendOnAwaiter" : "SuspendOnNotify", [site.Awaited]))
                    : runtime.Resolve(new RuntimeAsyncMember.ForAwaited(leafSuspension, task))),
        ]);
        sequence.AddRange([
            Op(CilOperation.LoadInt32, new CilOperand.ConstantI4(1)),
            Store(suspending),
            Branch(CilOperation.Leave, suspensionExit),
            Mark(site.Resume),
        ]);
        // Resumed here: what this suspension kept comes back from the frame, and a slot that can hold a reference lets
        // go of it, so a suspended frame keeps only what its suspension keeps.
        for (var index = 0; index < site.Saved.Length; index++)
        {
            var saved = site.Saved[index];
            var path = frames.Paths[slots[index]];
            var slot = new CilOperand.FieldInstance(path[^1]);
            sequence.AddRange([
                .. Owner(frame, path),
                Op(CilOperation.LoadField, slot),
                saved.Argument is { } argument
                    ? Op(CilOperation.StoreArgument, new CilOperand.Index(argument))
                    : Store(saved.Local!),
            ]);
            var type = path[^1].FieldType;
            if (!HoldsReferences(type)) continue;
            sequence.AddRange([
                .. Owner(frame, path),
                .. IsReference(type)
                    ? [Op(CilOperation.LoadNull), Op(CilOperation.StoreField, slot)]
                    : new[] { Op(CilOperation.LoadFieldAddress, slot), Op(CilOperation.InitializeObject, new CilOperand.TypeIdentity(type)) },
            ]);
        }
        if (viaVirtual)
        {
            // An override that did not take the call left its result in its task: it goes to the slot now.
            sequence.AddRange([
                Address(site.Temporary),
                Call(runtime.Resolve(new RuntimeAsyncMember.ForAwaited("VirtualResumed", task))),
            ]);
        }
        sequence.AddRange([
            Op(CilOperation.LoadInt32, new CilOperand.ConstantI4(0)),
            Store(state),
            Mark(done),
        ]);
        sequence.AddRange(site.Spills.Select(spill => Load(spill)));
        if (direct || viaVirtual)
        {
            // The callee's result, or its exception, from the slots its completion filled (for a virtual call, through
            // calls of its own, which an await that keeps no request turns back into GetResult).
            var result = site.Await.Signature.ReturnSignatureType;
            sequence.Add(Call(result.StackKind == CliValueKind.Void
                ? runtime.Resolve(Helper(viaVirtual ? "VirtualVoid" : "TakeVoid"))
                : runtime.Resolve(new RuntimeAsyncMember.Named(viaVirtual ? "VirtualResult" : "TakeResult", [result]))));
        }
        else if (!site.IsAwaiter)
        {
            sequence.AddRange(ReadAwaited(site, "GetResult", runtime));
        }
        return sequence;
    }

    // Whether a leaf await's awaited value has completed, or its result: of the temporary, which holds the task itself
    // where the await fused its ConfigureAwait (RuntimeAsyncAwaits). A value task, or a configured one, is read
    // through its address: passed by value, each read copied the struct into the helper's parameter, whose fields the
    // inlined helper then read in memory, and those whole copies kept every copy of the value task in the value frame.
    private static CilBodyElement[] ReadAwaited(Site site, string name, IRuntimeAsyncMemberResolver runtime)
    {
        var awaited = site.Temporary.Type;
        return IsValueTaskShape(awaited)
            ? [Address(site.Temporary), Call(runtime.Resolve(new RuntimeAsyncMember.ForAwaited(name + "At", awaited)))]
            : [Load(site.Temporary), Call(runtime.Resolve(new RuntimeAsyncMember.ForAwaited(name, awaited)))];
    }

    private static bool IsValueTaskShape(CliTypeIdentity awaited) =>
        IsValueTask(awaited) || DefinitionName(awaited) is
            "System.Runtime.CompilerServices.ConfiguredValueTaskAwaitable" or
            "System.Runtime.CompilerServices.ConfiguredValueTaskAwaitable`1";

    private static bool IsValueTask(CliTypeIdentity awaited) =>
        DefinitionName(awaited) is "System.Threading.Tasks.ValueTask" or "System.Threading.Tasks.ValueTask`1";

    private static string? DefinitionName(CliTypeIdentity type) =>
        type.Shape == CliTypeShape.GenericInstantiation ? type.ElementType!.FullName : type.FullName;

    // A branch to the target of the resume state's await (states count from 1), null for a state the dispatch cannot
    // carry here: at a loop's head or a protected block's start the state is nonzero only on the way to an await inside
    // it. A resumed activation's state always names one of its awaits, so one target needs no test: the chain ends in a
    // branch to the target most states share, or to `next` when a state goes on there (at the method's start, the first
    // loop or protected block may begin it). Without that, the entry's last test fell through to the method's start,
    // which the call that is not a resumption also reaches, and the structurer gave the two a dispatcher. The tests stay
    // a chain of equalities on the state, which wasm-opt turns into one br_table where it pays; a range test for states
    // sharing a target would stop that. The targets are loop entries the body also reaches, not regions of their own,
    // so the structurer's switch tables would not take them either.
    // The states dispatched here, in order, each with where it goes.
    private static IEnumerable<CilBodyElement> Dispatch(CilBodyLocal state,
        IReadOnlyList<(int State, CilBodyLabel Target)> targets, CilBodyLabel next)
    {
        var states = targets.Select(pair => (Target: (CilBodyLabel?)pair.Target, pair.State)).ToArray();
        var fallback = states.Any(pair => ReferenceEquals(pair.Target, next))
            ? next
            : states.GroupBy(pair => pair.Target, ReferenceEqualityComparer.Instance)
                .OrderByDescending(group => group.Count()).ThenBy(group => group.Min(pair => pair.State))
                .Select(group => (CilBodyLabel?)group.Key).FirstOrDefault() ?? next;
        return [
            .. states.Where(pair => !ReferenceEquals(pair.Target, fallback)).SelectMany(pair => new[]
            {
                Load(state),
                Op(CilOperation.LoadInt32, new CilOperand.ConstantI4(pair.State)),
                Branch(CilOperation.BranchIfEqual, pair.Target!),
            }),
            Branch(CilOperation.Branch, fallback),
        ];
    }

    // The method's awaits in order, numbered from 1.
    private ImmutableArray<Site> Sites(MethodInstanceModel method, CilMethodBody body, IRuntimeAsyncMemberResolver runtime,
        IReadOnlyDictionary<int, ImmutableArray<CliTypeIdentity?>> stacks)
    {
        var sites = ImmutableArray.CreateBuilder<Site>();
        var inHandler = InsideHandlers(body);
        foreach (var awaited in Classify(body, runtime.AsyncHelpers))
        {
            var instruction = body.Instructions[awaited.Index];
            if (!stacks.TryGetValue(instruction.Offset, out var stack))
                throw Unsupported(method, "has an await the runtime async lowering cannot reach");
            var spilled = stack[..^1];
            if (spilled.Any(type => type is null || type.Shape == CliTypeShape.ManagedByReference))
                throw Unsupported(method, "keeps a reference, or a value it cannot type, on the evaluation stack across an await" +
                    $" (IL_{instruction.Offset:x4}, under the awaited value: " +
                    string.Join(", ", spilled.Select(type => type?.ToString() ?? "a value of unknown type")) + ")");
            if (inHandler[awaited.Index])
            {
                throw Unsupported(method, "awaits inside an exception handler");
            }
            sites.Add(new(sites.Count + 1, instruction.Offset, awaited.Helper, awaited.Awaited, awaited.IsAwaiter,
                [.. spilled.Select(type => type!)])
            {
                DirectCallOffset = awaited.DirectCallOffset,
                VirtualCallOffset = awaited.VirtualCallOffset,
                DroppedOffsets = awaited.Dropped,
                ContinuesAnywhere = awaited.ContinuesAnywhere,
                FusedTask = awaited.FusedTask,
            });
        }
        return sites.ToImmutable();
    }

    // The target of the delegate a frame resumes through: none for a static method, the instance for a class's, and for a value type's a boxed copy
    // of this. Roslyn copies a value type's this into a local first thing, which the frame keeps, so the resumed call
    // never reads the box.
    private static IEnumerable<CilBodyElement> ResumerTarget(MethodInstanceModel method) =>
        method.Definition.IsStatic ? [Op(CilOperation.LoadNull)]
        : method.DeclaringType.IsValueType
            ? [Argument(0), Op(CilOperation.LoadObject, new CilOperand.TypeIdentity(method.DeclaringType)),
                Op(CilOperation.Box, new CilOperand.TypeIdentity(method.DeclaringType))]
            : [Argument(0)];

    // A value a suspension keeps: an argument (by its index, this counted) or a local of the lowered body.
    private sealed record SavedValue(CliTypeIdentity Type, int? Argument, CilBodyLocal? Local);

    // A lowered method's frame: its type (RuntimeAsyncFrame over its slots, parameters and result), the fields that lead
    // to each slot from the frame, and for each suspension (numbered from 1) the slot of each value it keeps.
    private sealed record FrameLayout(CliTypeIdentity Type, ImmutableArray<ImmutableArray<FieldInstanceModel>> Paths,
        ImmutableArray<ImmutableArray<int>> Sites);

    // A slot per value a suspension keeps, of the value's type; a value takes the next slot of its type that its
    // suspension has not taken, so suspensions share slots by type and the frame has the most of each that any keeps.
    // Upstream lays out a continuation per suspension and asks the VM for a type of that size and layout
    // (async.cpp's ContinuationLayoutBuilder and getContinuationType); a NetWasm frame serves the whole activation, so
    // its type covers every suspension.
    private static FrameLayout Frame(IRuntimeAsyncFrameLayoutResolver frames, ImmutableArray<Site> sites,
        ImmutableArray<CliTypeIdentity> parameters, CliTypeIdentity returnType)
    {
        var types = new List<CliTypeIdentity>();
        var byType = new Dictionary<string, List<int>>(StringComparer.Ordinal);
        var siteSlots = ImmutableArray.CreateBuilder<ImmutableArray<int>>();
        foreach (var site in sites)
        {
            var taken = new Dictionary<string, int>(StringComparer.Ordinal);
            var slots = ImmutableArray.CreateBuilder<int>();
            foreach (var saved in site.Saved)
            {
                var type = SlotType(saved.Type);
                var occurrence = taken.GetValueOrDefault(type.CanonicalName);
                taken[type.CanonicalName] = occurrence + 1;
                if (!byType.TryGetValue(type.CanonicalName, out var ofType)) byType.Add(type.CanonicalName, ofType = []);
                if (occurrence == ofType.Count)
                {
                    ofType.Add(types.Count);
                    types.Add(type);
                }
                slots.Add(ofType[occurrence]);
            }
            siteSlots.Add(slots.ToImmutable());
        }
        var layout = frames.Resolve([.. types], parameters, returnType);
        return new(layout.Type, layout.Paths, siteSlots.ToImmutable());
    }

    // The frame and the address of the struct that holds a slot, ready for the slot's field.
    private static IEnumerable<CilBodyElement> Owner(CilBodyLocal frame, ImmutableArray<FieldInstanceModel> path) =>
        [Load(frame), .. path[..^1].Select(field => Op(CilOperation.LoadFieldAddress, new CilOperand.FieldInstance(field)))];

    // A slot's type: the value's own without its custom modifiers, but a native int for a pointer, which no generic
    // argument can be.
    private static CliTypeIdentity SlotType(CliTypeIdentity type)
    {
        while (type.Shape == CliTypeShape.Modified && type.ElementType is { } inner) type = inner;
        return type.Shape is CliTypeShape.UnmanagedPointer or CliTypeShape.FunctionPointer ? NativeInt : type;
    }

    // Whether a slot can keep an object alive: anything but a primitive scalar (a struct or a type parameter may).
    private static bool HoldsReferences(CliTypeIdentity type) =>
        IsTypeParameter(type) || type.StackKind is not (CliValueKind.I4 or CliValueKind.I8 or CliValueKind.F4 or
            CliValueKind.F8 or CliValueKind.NativeInt or CliValueKind.V128);

    private static bool IsReference(CliTypeIdentity type) =>
        !IsTypeParameter(type) && type.StackKind == CliValueKind.ManagedReference;

    private static bool IsTypeParameter(CliTypeIdentity type) =>
        type.Shape is CliTypeShape.GenericTypeParameter or CliTypeShape.GenericMethodParameter;

    // Whether each instruction lies in a handler or a filter: each range marked at its ends, then one running count.
    private static bool[] InsideHandlers(CilMethodBody body)
    {
        var instructions = body.Instructions;
        var positions = new Dictionary<int, int>(instructions.Length + 1);
        for (var position = 0; position < instructions.Length; position++) positions[instructions[position].Offset] = position;
        if (instructions.Length > 0) positions[instructions[^1].NextOffset] = instructions.Length;
        var marks = new int[instructions.Length + 1];
        void Mark(int start, int end)
        {
            if (!positions.TryGetValue(start, out var first) || !positions.TryGetValue(end, out var last)) return;
            marks[first]++;
            marks[last]--;
        }
        foreach (var region in body.ExceptionRegions)
        {
            Mark(region.HandlerOffset, region.HandlerOffset + region.HandlerLength);
            if (region.FilterOffset is { } filter) Mark(filter, region.HandlerOffset);
        }
        var inside = new bool[instructions.Length];
        var depth = 0;
        for (var position = 0; position < instructions.Length; position++)
        {
            depth += marks[position];
            inside[position] = depth > 0;
        }
        return inside;
    }

    // The points a resume point is dispatched through: each branch back to an earlier instruction makes a loop from its
    // target to the branch (the decoder has lowered every switch into branches), and each protected block that holds an
    // await makes one from its start; points that start at one offset merge.
    private static ImmutableArray<Point> Points(CilMethodBody body, ImmutableArray<Site> sites)
    {
        var latches = new Dictionary<int, int>();
        foreach (var instruction in body.Instructions)
        {
            if (instruction.Operand is CilOperand.BranchTarget { Offset: var target } && target <= instruction.Offset)
                latches[target] = Math.Max(latches.GetValueOrDefault(target, instruction.Offset), instruction.Offset);
        }
        var protectedStarts = new HashSet<int>();
        foreach (var region in body.ExceptionRegions)
        {
            var last = region.TryOffset + region.TryLength - 1;
            latches[region.TryOffset] = Math.Max(latches.GetValueOrDefault(region.TryOffset, last), last);
            protectedStarts.Add(region.TryOffset);
        }
        var offsets = SiteOffsets(sites);
        var heads = new Lazy<CilLoopHeads>(() => new CilLoopHeads(body));
        var points = latches
            .Where(pair => HasSiteIn(offsets, pair.Key, pair.Value + 1))
            .Select(pair => protectedStarts.Contains(pair.Key)
                ? new Point(pair.Key, pair.Value, true, pair.Key)
                : new Point(pair.Key, pair.Value, false, heads.Value.HeadOf(pair.Key, pair.Value)))
            .ToArray();
        // Loops entered at one place (one inside the other, both entered by a branch to it) keep their starts, where
        // the dispatch passes through one at a time.
        return points.Select(point => point.Head).Distinct().Count() == points.Length
            ? [.. points]
            : [.. points.Select(point => point with { Head = point.Start })];
    }

    // Protected blocks that start where an enclosing one does but hold fewer of its awaits (C#'s
    // `try { try { } finally { } await ...; }`, which Roslyn's IL starts at one offset): the enclosing block's dispatch
    // reaches awaits outside them, so it runs before they start. Such blocks, innermost last, each start at a label of
    // their own after the enclosing dispatch, and one that holds awaits is a point of its own, entered as any protected
    // block is. Blocks that hold the same awaits share a start. Nested blocks hold no more awaits than the ones around
    // them, so only the innermost may hold none.
    private sealed record ProtectedRun(Point? Point, CilBodyLabel Start, ImmutableArray<int> Regions);

    private static ImmutableDictionary<int, ImmutableArray<ProtectedRun>> NestedRuns(CilMethodBody body,
        ImmutableArray<Site> sites, ImmutableArray<Point> points)
    {
        var nested = ImmutableDictionary.CreateBuilder<int, ImmutableArray<ProtectedRun>>();
        var offsets = SiteOffsets(sites);
        var protectedStarts = points.Where(point => point.IsProtected).Select(point => point.Start).ToHashSet();
        foreach (var byStart in body.ExceptionRegions
            .Select((region, index) => (Region: region, Index: index))
            .GroupBy(pair => pair.Region.TryOffset))
        {
            var start = byStart.Key;
            if (!protectedStarts.Contains(start)) continue;
            // A block's awaits are a run of the method's, which are in offset order: their count names them.
            var runs = new List<(int Length, List<int> Regions, int Awaits)>();
            foreach (var group in byStart.GroupBy(pair => pair.Region.TryLength).OrderByDescending(group => group.Key))
            {
                var awaits = SiteCount(offsets, start, start + group.Key);
                if (runs.Count != 0 && runs[^1].Awaits == awaits)
                {
                    runs[^1].Regions.AddRange(group.Select(pair => pair.Index));
                    continue;
                }
                runs.Add((group.Key, [.. group.Select(pair => pair.Index)], awaits));
            }
            if (runs.Count < 2) continue;
            nested[start] = [.. runs.Skip(1).Select(run => new ProtectedRun(
                run.Awaits == 0 ? null : new Point(start, start + run.Length - 1, true, start),
                new CilBodyLabel(),
                [.. run.Regions]))];
        }
        return nested.ToImmutable();
    }

    // Each await's enclosing points, outermost first (longest, then earliest), from one sweep over the awaits in offset
    // order with the points open at each: points open at their start and close past their end, kept ordered as the
    // chain is. Then, per point, the awaits it holds and its place in each one's chain.
    private static (List<int>[] Chains, List<(int Site, int Position)>[] Held) Enclosings(
        ImmutableArray<Site> sites, ImmutableArray<Point> points)
    {
        var order = Comparer<int>.Create((left, right) =>
        {
            var bySize = (points[right].End - points[right].Start).CompareTo(points[left].End - points[left].Start);
            if (bySize != 0) return bySize;
            var byStart = points[left].Start.CompareTo(points[right].Start);
            return byStart != 0 ? byStart : left.CompareTo(right);
        });
        var open = new SortedSet<int>(order);
        var closing = new PriorityQueue<int, int>();
        var byStart = Enumerable.Range(0, points.Length).OrderBy(index => points[index].Start).ToArray();
        var chains = new List<int>[sites.Length];
        var held = new List<(int Site, int Position)>[points.Length];
        for (var index = 0; index < points.Length; index++) held[index] = [];
        var next = 0;
        for (var site = 0; site < sites.Length; site++)
        {
            var offset = sites[site].Offset;
            while (next < byStart.Length && points[byStart[next]].Start <= offset)
            {
                open.Add(byStart[next]);
                closing.Enqueue(byStart[next], points[byStart[next]].End);
                next++;
            }
            while (closing.TryPeek(out var point, out var end) && end < offset)
            {
                closing.Dequeue();
                open.Remove(point);
            }
            var chain = new List<int>(open);
            chains[site] = chain;
            for (var position = 0; position < chain.Count; position++) held[chain[position]].Add((site, position));
        }
        return (chains, held);
    }

    // The await offsets, in order: the awaits are collected in instruction order.
    private static int[] SiteOffsets(ImmutableArray<Site> sites) => [.. sites.Select(site => site.Offset)];

    // How many awaits lie in [start, end).
    private static int SiteCount(int[] offsets, int start, int end) => LowerBound(offsets, end) - LowerBound(offsets, start);

    private static bool HasSiteIn(int[] offsets, int start, int end) => SiteCount(offsets, start, end) > 0;

    private static int LowerBound(int[] values, int value)
    {
        var low = 0;
        var high = values.Length;
        while (low < high)
        {
            var middle = low + (high - low) / 2;
            if (values[middle] < value) low = middle + 1;
            else high = middle;
        }
        return low;
    }

    private static MethodInstanceModel Completion(IRuntimeAsyncMemberResolver runtime, string name, ResultShape shape,
        CliTypeIdentity? result) => RuntimeAsyncCompletions.Resolve(runtime, name, shape, result);

    private static MethodInstanceModel Faulted(IRuntimeAsyncMemberResolver runtime, ResultShape shape, CliTypeIdentity? result) =>
        RuntimeAsyncCompletions.Resolve(runtime, "Faulted", shape, result);

    private static RuntimeAsyncMember Helper(string name) => RuntimeAsyncMember.Helper(name);

    // A ref struct local, which cannot be boxed into the frame.
    private bool IsByRefLike(CliTypeIdentity type) =>
        type.IsValueType && type.Shape is CliTypeShape.Named or CliTypeShape.GenericInstantiation &&
        definitions.ResolveTypeIdentity(type).IsByRefLike;

    private CompilerException Unsupported(MethodInstanceModel method, string problem) => new(new CompilerDiagnostic(
        DiagnosticCode.UnsupportedCil,
        $"is a runtime-async method (MethodImplOptions.Async) that {problem}, which NetWasm's runtime async lowering " +
        "does not handle yet; build its assembly without runtime-async=on",
        symbols.Format(method.Definition)));
}
