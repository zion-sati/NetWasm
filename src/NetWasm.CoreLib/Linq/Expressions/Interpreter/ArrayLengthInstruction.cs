// Portions derived from dotnet/runtime System.Linq.Expressions at commit
// deae2e32bf0e018e09d64597b7c59b62b73bb3ce (Interpreter/ArrayOperations.cs).
// Licensed to the .NET Foundation under one or more agreements. The .NET
// Foundation licenses this file to you under the MIT license.

namespace System.Linq.Expressions.Interpreter
{
    // ArrayLength: a null array throws NullReferenceException, as ldlen does.
    internal sealed class ArrayLengthInstruction : Instruction
    {
        internal override int ConsumedStack => 1;

        internal override int ProducedStack => 1;

        internal override int Run(InterpretedFrame frame)
        {
            frame.Push(((Array)frame.Pop()!).Length);
            return 1;
        }
    }
}
