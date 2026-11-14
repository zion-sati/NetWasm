using System;
using System.Collections.Generic;
using NetWasm.Compiler.ControlFlow.Structured;

namespace NetWasm.Compiler.Wasm.Emission.Values.AggregateAnalysis;

internal sealed class StructuredLoopDepthMeasurer : IStructuredLoopDepthMeasurer
{
    public Dictionary<StructuredBlockId, int> Measure(StructuredSequence body)
    {
        ArgumentNullException.ThrowIfNull(body);
        var depths = new Dictionary<StructuredBlockId, int>();
        Visit(body, 0);
        return depths;

        void Add(StructuredBlockOccurrence? occurrence, int depth)
        {
            if (occurrence is not null)
            {
                depths[occurrence.Block] = Math.Max(depths.GetValueOrDefault(occurrence.Block), depth);
            }
        }

        void Visit(StructuredSequence sequence, int depth)
        {
            foreach (var region in sequence.Regions)
            {
                switch (region)
                {
                    case StructuredCode code:
                        Add(code.Occurrence, depth);
                        break;
                    case StructuredIf conditional:
                        Add(conditional.Condition, depth);
                        Visit(conditional.WhenTrue, depth);
                        Visit(conditional.WhenFalse, depth);
                        break;
                    case StructuredLoop loop:
                        Add(loop.Condition, depth + 1);
                        Visit(loop.Body, depth + 1);
                        Visit(loop.ContinueBody, depth + 1);
                        Visit(loop.ExitBody, depth);
                        break;
                    case StructuredPostTestLoop loop:
                        Add(loop.Condition, depth + 1);
                        Visit(loop.Body, depth + 1);
                        Visit(loop.ContinueBody, depth + 1);
                        Visit(loop.ExitBody, depth);
                        break;
                    case StructuredLabeledBlock labeled:
                        Visit(labeled.Body, depth);
                        break;
                    case StructuredLabeledLoop labeled:
                        Visit(labeled.Body, depth + 1);
                        break;
                    case StructuredDispatcher dispatcher:
                        foreach (var block in dispatcher.Blocks)
                        {
                            Add(block.Occurrence, depth + 1);
                        }
                        foreach (var exit in dispatcher.Exits)
                        {
                            Visit(exit.Body, depth);
                        }
                        break;
                }
            }
        }
    }
}
