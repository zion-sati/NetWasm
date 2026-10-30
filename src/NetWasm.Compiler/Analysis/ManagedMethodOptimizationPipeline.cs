using System;
using System.Collections.Immutable;
using NetWasm.Compiler.Analysis.AddressTakenValues;
using NetWasm.Compiler.Analysis.Inlining;
using NetWasm.Compiler.Analysis.Arrays;
using NetWasm.Compiler.Analysis.Devirtualization;
using NetWasm.Compiler.Analysis.Loops;
using NetWasm.Compiler.ControlFlow;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Analysis;

internal sealed class ManagedMethodOptimizationPipeline(
    IManagedMethodOptimizer staticDataViews,
    IMethodSpecializer constantCalls,
    IManagedMethodInliner inliner,
    IControlFlowGraphBuilder graphBuilder,
    ITypedStackValidator stackValidator,
    ISimpleValueTypeAccessorCanonicalizer accessors,
    IArrayBoundsCheckEliminator arrayBounds,
    IArrayLoopUnroller unroller,
    IRepeatedArrayAccessEliminator repeatedAccesses,
    IExactReceiverDevirtualizer devirtualizer,
    IMethodSpecializer allocations,
    IRvaSpanLocalEliminator rvaSpans,
    ILocalAddressDereferenceFolder addressDereferences,
    IIdempotentLoopCollapser idempotentLoops,
    IArrayLoopIdiomRewriter arrayLoops,
    IReductionLoopRewriter reductionLoops,
    ILoopInvariantCallHoister invariantCalls,
    IElementwiseLoopVectorizer vectorizer,
    ILoopStoreForwarder loopStores,
    IBeforeFieldInitEntryInitializer entryInitialization,
    IEmptyFinallyRemover emptyFinallies,
    IStructLocalScalarReplacer structLocals,
    Allocations.INonEscapingAllocationReplacer objects) : IManagedMethodOptimizer
{
    private readonly IManagedMethodOptimizer _staticDataViews = staticDataViews ??
        throw new ArgumentNullException(nameof(staticDataViews));
    private readonly IMethodSpecializer _constantCalls = constantCalls ??
        throw new ArgumentNullException(nameof(constantCalls));
    private readonly IManagedMethodInliner _inliner = inliner ??
        throw new ArgumentNullException(nameof(inliner));
    private readonly IControlFlowGraphBuilder _graphBuilder = graphBuilder ??
        throw new ArgumentNullException(nameof(graphBuilder));
    private readonly ITypedStackValidator _stackValidator = stackValidator ??
        throw new ArgumentNullException(nameof(stackValidator));
    private readonly ISimpleValueTypeAccessorCanonicalizer _accessors = accessors ??
        throw new ArgumentNullException(nameof(accessors));
    private readonly IArrayBoundsCheckEliminator _arrayBounds = arrayBounds ??
        throw new ArgumentNullException(nameof(arrayBounds));
    private readonly IArrayLoopUnroller _unroller = unroller ?? throw new ArgumentNullException(nameof(unroller));
    private readonly IRepeatedArrayAccessEliminator _repeatedAccesses = repeatedAccesses ??
        throw new ArgumentNullException(nameof(repeatedAccesses));
    private readonly IExactReceiverDevirtualizer _devirtualizer = devirtualizer ??
        throw new ArgumentNullException(nameof(devirtualizer));
    private readonly IMethodSpecializer _allocations = allocations ?? throw new ArgumentNullException(nameof(allocations));
    private readonly IRvaSpanLocalEliminator _rvaSpans = rvaSpans ?? throw new ArgumentNullException(nameof(rvaSpans));
    private readonly ILocalAddressDereferenceFolder _addressDereferences = addressDereferences ??
        throw new ArgumentNullException(nameof(addressDereferences));
    private readonly IIdempotentLoopCollapser _idempotentLoops = idempotentLoops ??
        throw new ArgumentNullException(nameof(idempotentLoops));
    private readonly IArrayLoopIdiomRewriter _arrayLoops = arrayLoops ?? throw new ArgumentNullException(nameof(arrayLoops));
    private readonly IReductionLoopRewriter _reductionLoops = reductionLoops ??
        throw new ArgumentNullException(nameof(reductionLoops));
    private readonly ILoopInvariantCallHoister _invariantCalls = invariantCalls ??
        throw new ArgumentNullException(nameof(invariantCalls));
    private readonly IElementwiseLoopVectorizer _vectorizer = vectorizer ?? throw new ArgumentNullException(nameof(vectorizer));
    private readonly ILoopStoreForwarder _loopStores = loopStores ?? throw new ArgumentNullException(nameof(loopStores));
    private readonly IBeforeFieldInitEntryInitializer _entryInitialization = entryInitialization ??
        throw new ArgumentNullException(nameof(entryInitialization));
    private readonly IEmptyFinallyRemover _emptyFinallies = emptyFinallies ??
        throw new ArgumentNullException(nameof(emptyFinallies));
    private readonly IStructLocalScalarReplacer _structLocals = structLocals ??
        throw new ArgumentNullException(nameof(structLocals));
    private readonly Allocations.INonEscapingAllocationReplacer _objects = objects ??
        throw new ArgumentNullException(nameof(objects));

    // Inlined callees bring their own calls, so a few rounds reach helpers of
    // helpers. Each round revalidates the body and re-enters static data view
    // elimination, which recognizes producers only while they are still calls.
    internal const int MaximumInliningDepth = 3;

    public CilMethodBody Optimize(ManagedMethodBody method)
    {
        ArgumentNullException.ThrowIfNull(method);
        var optimized = _staticDataViews.Optimize(method);
        var entryStacks = ReferenceEquals(optimized, method.Body)
            ? method.ControlFlow.InstructionEntryStacks
            : ImmutableDictionary<int, ImmutableArray<CliValueKind>>.Empty;
        var settled = false;
        for (var depth = 0; depth < MaximumInliningDepth; depth++)
        {
            // A loop that folds span elements is found while the indexer is a
            // call, before inlining and accessor canonicalization replace it,
            // and a pure call of values a loop does not change leaves the loop
            // before inlining puts its body there.
            var reduced = _reductionLoops.Rewrite(_invariantCalls.Rewrite(optimized));
            if (!ReferenceEquals(reduced, optimized))
            {
                optimized = reduced;
                entryStacks = ImmutableDictionary<int, ImmutableArray<CliValueKind>>.Empty;
            }
            // A field accessor becomes the field access it performs, so inlining
            // sees a read of the value rather than a call given its address. The
            // replacement moves the same values, so the entry stacks still hold.
            optimized = _accessors.Canonicalize(optimized);
            // A constant argument is spilled to a local once its call is inlined, so fold first.
            var folded = _constantCalls.Rewrite(optimized);
            if (!ReferenceEquals(folded, optimized))
            {
                optimized = folded;
                entryStacks = ImmutableDictionary<int, ImmutableArray<CliValueKind>>.Empty;
            }
            // A direct call keeps the stack shape of the virtual call it replaces.
            optimized = _devirtualizer.Rewrite(optimized, entryStacks);
            var inlined = _inliner.Inline(
                method.Method,
                optimized,
                entryStacks,
                nested: depth > 0);
            if (ReferenceEquals(inlined, optimized))
            {
                settled = true;
                break;
            }

            var validated = _stackValidator.Validate(_graphBuilder.Build(inlined));
            optimized = _staticDataViews.Optimize(new(method.Method, validated));
            entryStacks = ReferenceEquals(optimized, inlined)
                ? validated.InstructionEntryStacks
                : ImmutableDictionary<int, ImmutableArray<CliValueKind>>.Empty;
        }
        // The last round's inlined calls have not been devirtualized yet.
        if (!settled)
        {
            optimized = _devirtualizer.Rewrite(optimized, ImmutableDictionary<int, ImmutableArray<CliValueKind>>.Empty);
        }
        // Inlined callees leave receivers and allocations that nothing reads,
        // objects reached only for their fields, finally handlers with nothing
        // to run, struct locals used field by field, reads and writes through a
        // local's address, counted loops that only repeat their first iteration,
        // array fill and copy loops whose iterations run in bulk first, element
        // loops whose iterations run as vectors first, loop reloads of an element
        // the loop itself stores, and span reads that RVA data answers. The entry checks go in before
        // the array passes, whose proofs then describe the final instructions.
        return _repeatedAccesses.Rewrite(_unroller.Rewrite(_arrayBounds.Rewrite(_entryInitialization.Rewrite(
            _rvaSpans.Rewrite(_loopStores.Rewrite(_vectorizer.Rewrite(_arrayLoops.Rewrite(_idempotentLoops.Rewrite(_addressDereferences.Rewrite(_structLocals.Rewrite(_accessors.Canonicalize(
                _emptyFinallies.Rewrite(_objects.Rewrite(_allocations.Rewrite(optimized)))))))))))))));
    }
}
