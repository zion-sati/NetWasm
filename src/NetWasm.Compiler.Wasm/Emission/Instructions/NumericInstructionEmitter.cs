// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Immutable;

namespace NetWasm.Compiler.Wasm.Emission.Instructions;

internal sealed class NumericInstructionEmitter(
    NumericOperatorEmitter operators,
    NumericConversionInstructionEmitter conversions,
    NumericComparisonInstructionEmitter comparisons,
    ExceptionalNumericInstructionEmitter exceptional,
    ConditionalSelectEmitter selects) : InstructionCommandProvider
{
    public override ImmutableArray<InstructionCommand> Commands =>
    [
        .. operators.Commands,
        .. conversions.Commands,
        .. comparisons.Commands,
        .. exceptional.Commands,
        .. selects.Commands,
    ];
}
