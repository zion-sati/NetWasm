using System;
using NetWasm.Compiler.ControlFlow;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Analysis.NullChecks;

internal interface IConstructedObjectNullCheckEliminator
{
    CilMethodBody Rewrite(CilMethodBody body);
}

// Adds the accesses of objects newobj produced to the body's proof of non-null
// references, which the emitters consult before writing a null check. Runs after
// every pass that rewrites instructions, so the proof describes the emitted body.
internal sealed class ConstructedObjectNullCheckEliminator(
    IControlFlowGraphBuilder graphs,
    ITypedStackValidator stacks,
    IConstructedReceiverPlanner planner,
    bool enabled) : IConstructedObjectNullCheckEliminator
{
    private readonly IControlFlowGraphBuilder _graphs = graphs ?? throw new ArgumentNullException(nameof(graphs));
    private readonly ITypedStackValidator _stacks = stacks ?? throw new ArgumentNullException(nameof(stacks));
    private readonly IConstructedReceiverPlanner _planner = planner ?? throw new ArgumentNullException(nameof(planner));

    public CilMethodBody Rewrite(CilMethodBody body)
    {
        ArgumentNullException.ThrowIfNull(body);
        if (!enabled) return body;
        var nonNull = _planner.Plan(_stacks.Validate(_graphs.Build(body)), thisIsNonNull: false);
        return nonNull.IsEmpty ? body : NonNullProof.Add(body, nonNull);
    }
}
