using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Analysis;

// The locals and arguments whose address a method takes anywhere. A ref, span
// or pointer taken earlier, or one a call keeps, can read or write such a
// variable wherever it reaches, so an optimization that keeps one's value, or
// splits it, between two of its instructions must leave it alone.
internal static class ExposedSlots
{
    public static HashSet<(bool Argument, int Index)> Of(ImmutableArray<CilInstruction> instructions) =>
    [
        .. instructions
            .Where(static instruction => instruction.Operation is CilOperation.LoadLocalAddress or CilOperation.LoadArgumentAddress)
            .Select(static instruction => (instruction.Operation == CilOperation.LoadArgumentAddress,
                ((CilOperand.Index)instruction.Operand).Value)),
    ];
}
