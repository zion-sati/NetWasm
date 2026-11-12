using System;
using System.Linq;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.EntryPoints;

// Whether a method body reads an argument: loads it or takes its address. A body that only stores
// to it, or never names it, cannot observe the value its caller passed.
internal sealed class ArgumentReadDetector : IArgumentReadDetector
{
    public bool Detect(CilMethodBody body, int argument)
    {
        ArgumentNullException.ThrowIfNull(body);
        ArgumentOutOfRangeException.ThrowIfNegative(argument);
        return body.Instructions.Any(instruction =>
            instruction.Operation is CilOperation.LoadArgument or CilOperation.LoadArgumentAddress &&
            instruction.Operand is CilOperand.Index { Value: var index } &&
            index == argument);
    }
}
