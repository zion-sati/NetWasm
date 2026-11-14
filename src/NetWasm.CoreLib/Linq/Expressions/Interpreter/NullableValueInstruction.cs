// Portions derived from dotnet/runtime System.Linq.Expressions at commit
// deae2e32bf0e018e09d64597b7c59b62b73bb3ce.
// Licensed to the .NET Foundation under one or more agreements. The .NET
// Foundation licenses this file to you under the MIT license.

namespace System.Linq.Expressions.Interpreter
{
    internal sealed class NullableValueInstruction : Instruction
    {
        internal override int ConsumedStack => 1;

        internal override int ProducedStack => 1;

        internal override int Run(InterpretedFrame frame)
        {
            var value = frame.Pop();
            if (value is null)
            {
                throw new InvalidOperationException("Nullable object must have a value.");
            }
            frame.Push(value);
            return 1;
        }
    }
}
