// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.ControlFlow.Draft;
using System.Collections.Generic;
using System.Collections.Immutable;

namespace NetWasm.Compiler.ControlFlow.Structuring;

internal sealed class StructuredSequenceStepChain(
    ImmutableArray<IStructuredSequenceStepExecutor> steps) : IStructuredSequenceStepExecutor
{
    public StructuredSequenceStepResult Execute(
        IStructuredControlFlowBuilder structuredControlFlowBuilder,
        ControlFlowStructuringState state,
        int currentBlockOffset,
        int? stop,
        ImmutableHashSet<int> allowed,
        HashSet<int> path,
        ImmutableArray<StructuredRegionDraft>.Builder regions)
    {
        foreach (var step in steps)
        {
            var result = step.Execute(
                structuredControlFlowBuilder,
                state,
                currentBlockOffset,
                stop,
                allowed,
                path,
                regions);
            if (result.Disposition != StructuredSequenceStepDisposition.NotHandled)
            {
                return result;
            }
        }

        return new StructuredSequenceStepResult(
            StructuredSequenceStepDisposition.NotHandled,
            currentBlockOffset);
    }
}
