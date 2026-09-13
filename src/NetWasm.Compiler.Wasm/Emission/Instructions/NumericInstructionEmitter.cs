using System.Collections.Immutable;

namespace NetWasm.Compiler.Wasm.Emission.Instructions;

internal sealed class NumericInstructionEmitter(
    NumericOperatorEmitter operators,
    NumericConversionInstructionEmitter conversions,
    NumericComparisonInstructionEmitter comparisons,
    ExceptionalNumericInstructionEmitter exceptional) : InstructionCommandProvider
{
    public override ImmutableArray<InstructionCommand> Commands =>
    [
        .. operators.Commands,
        .. conversions.Commands,
        .. comparisons.Commands,
        .. exceptional.Commands,
    ];
}
