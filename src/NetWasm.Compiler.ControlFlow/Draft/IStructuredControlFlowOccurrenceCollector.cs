using System.Collections.Immutable;

namespace NetWasm.Compiler.ControlFlow.Draft;

internal interface IStructuredControlFlowOccurrenceCollector
{
    ImmutableArray<StructuredControlFlowBlockOccurrence> Collect(StructuredSequenceDraft sequence);
}
