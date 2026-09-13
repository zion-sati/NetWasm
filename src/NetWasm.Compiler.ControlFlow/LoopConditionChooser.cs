using System;
using System.Linq;

namespace NetWasm.Compiler.ControlFlow;

public sealed class LoopConditionChooser : ILoopConditionChooser
{
    public int? Choose(LoopConditionSelection selection)
    {
        ArgumentNullException.ThrowIfNull(selection);
        if (IsBoundaryCondition(selection, selection.Header))
        {
            return selection.Header;
        }
        var candidates = selection.Component
            .Where(node => IsBoundaryCondition(selection, node))
            .Order()
            .ToArray();
        if (candidates.Length == 1)
        {
            return candidates[0];
        }
        return null;
    }

    private static bool IsBoundaryCondition(
        LoopConditionSelection selection,
        int node) =>
        selection.Graph.Successors[node].Length == 2 &&
        selection.Graph.Successors[node].Count(selection.Component.Contains) == 1;

}
