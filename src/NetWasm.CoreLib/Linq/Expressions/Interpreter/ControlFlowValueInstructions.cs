// Portions derived from dotnet/runtime System.Linq.Expressions at commit
// deae2e32bf0e018e09d64597b7c59b62b73bb3ce (Interpreter/ControlFlowInstructions.cs).
// Licensed to the .NET Foundation under one or more agreements. The .NET
// Foundation licenses this file to you under the MIT license.

namespace System.Linq.Expressions.Interpreter
{
    // Throw: throws the exception on the stack; a null one throws NullReferenceException, as the throw instruction does.
    internal sealed class ThrowInstruction : Instruction
    {
        internal override int ConsumedStack => 1;

        internal override int Run(InterpretedFrame frame) =>
            throw (Exception?)frame.Pop() ?? new NullReferenceException();
    }

    // A switch case test without a comparison method: the values' own equality, which for the types a switch tests
    // (integers, characters, Booleans, enums, strings, and their nullable forms) is the Equal operator's result.
    internal sealed class ValueEqualsInstruction : Instruction
    {
        internal override int ConsumedStack => 2;

        internal override int ProducedStack => 1;

        internal override int Run(InterpretedFrame frame)
        {
            var right = frame.Pop();
            var left = frame.Pop();
            frame.Push(Equals(left, right));
            return 1;
        }
    }
}
