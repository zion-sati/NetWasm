namespace NetWasm.Compiler.ControlFlow.Draft;

internal sealed record StructuredControlFlowBlockOccurrence(
    int Block,
    int Ordinal,
    bool Preferred);
