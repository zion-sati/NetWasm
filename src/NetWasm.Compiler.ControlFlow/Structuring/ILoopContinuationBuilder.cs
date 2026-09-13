using NetWasm.Compiler.ControlFlow.Draft;
namespace NetWasm.Compiler.ControlFlow.Structuring;

internal interface ILoopContinuationBuilder
{
    StructuredSequenceDraft Build(ControlFlowStructuringState state, int offset, int length, LoopRegion loop);
}
