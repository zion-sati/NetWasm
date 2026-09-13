using NetWasm.Compiler.ControlFlow.Draft;
namespace NetWasm.Compiler.ControlFlow.Structuring;

internal interface IBlockRangeStructurer
{
    StructuredSequenceDraft Structure(
        ControlFlowStructuringState state,
        int offset,
        int length);
}
