using System.Collections.Immutable;

namespace NetWasm.Compiler.ControlFlow.Structured;

internal interface IStructuredBlockDefinitionFactory
{
    ImmutableDictionary<StructuredBlockId, StructuredBlockDefinition> Create(
        ValidatedControlFlowGraph validated);
}
