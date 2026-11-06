using System;
using NetWasm.Compiler.ControlFlow;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Analysis.Arrays;

internal interface IRepeatedArrayAccessEliminator
{
    CilMethodBody Rewrite(CilMethodBody body);
}

// Runs after every pass that rewrites instructions, so its proof describes the
// emitted body. It adds to the loop proof of the bounds-check eliminator when
// that proof still matches.
internal sealed class RepeatedArrayAccessEliminator(
    IControlFlowGraphBuilder graphs,
    ITypedStackValidator stacks,
    IRepeatedArrayAccessPlanner planner,
    bool enabled) : IRepeatedArrayAccessEliminator
{
    private readonly IControlFlowGraphBuilder _graphs = graphs ?? throw new ArgumentNullException(nameof(graphs));
    private readonly ITypedStackValidator _stacks = stacks ?? throw new ArgumentNullException(nameof(stacks));
    private readonly IRepeatedArrayAccessPlanner _planner = planner ?? throw new ArgumentNullException(nameof(planner));

    public CilMethodBody Rewrite(CilMethodBody body)
    {
        ArgumentNullException.ThrowIfNull(body);
        if (!enabled) return body;
        var plan = _planner.Plan(_stacks.Validate(_graphs.Build(body)));
        if (plan.Checked.IsEmpty && plan.NonNull.IsEmpty && plan.CheckedDimensions.IsEmpty) return body;
        var proof = body.ArrayBoundsProof is { } existing &&
            existing.Describes(body.Instructions, body.Locals, body.LocalSignatureTypes)
            ? existing
            : new ArrayBoundsProof(body.Instructions, body.Locals, body.LocalSignatureTypes, []);
        return body with
        {
            ArrayBoundsProof = proof with
            {
                AccessOffsets = proof.AccessOffsets.Union(plan.Checked),
                NonNullOffsets = proof.NonNullOffsets.Union(plan.NonNull),
                CheckedDimensions = proof.CheckedDimensions.Union(plan.CheckedDimensions),
            },
        };
    }
}
