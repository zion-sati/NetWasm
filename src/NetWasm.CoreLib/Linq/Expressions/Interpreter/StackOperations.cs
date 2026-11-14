// Portions derived from dotnet/runtime System.Linq.Expressions at commit
// deae2e32bf0e018e09d64597b7c59b62b73bb3ce.
// Licensed to the .NET Foundation under one or more agreements. The .NET
// Foundation licenses this file to you under the MIT license.

namespace System.Linq.Expressions.Interpreter
{
    internal sealed class LoadObjectInstruction(object? value) : Instruction
    {
        internal override int ProducedStack => 1;

        internal override int Run(InterpretedFrame frame)
        {
            frame.Push(value);
            return 1;
        }
    }

    // A new box of a value type's zero value on each run, which a member initializer or a mutating call may change;
    // a one-element array's element, as the interpreter's other defaults are.
    internal sealed class NewDefaultValueInstruction(Type type) : Instruction
    {
        internal override int ProducedStack => 1;

        internal override int Run(InterpretedFrame frame)
        {
            frame.Push(Array.CreateInstance(type, 1).GetValue(0));
            return 1;
        }
    }

    // The top value again, for an initializer's next member or element.
    internal sealed class DupInstruction : Instruction
    {
        internal static readonly DupInstruction Instance = new();

        private DupInstruction()
        {
        }

        internal override int ConsumedStack => 1;

        internal override int ProducedStack => 2;

        internal override int Run(InterpretedFrame frame)
        {
            frame.Push(frame.Peek());
            return 1;
        }
    }

    internal sealed class PopInstruction : Instruction
    {
        internal static readonly PopInstruction Instance = new();

        private PopInstruction()
        {
        }

        internal override int ConsumedStack => 1;

        internal override int Run(InterpretedFrame frame)
        {
            frame.Pop();
            return 1;
        }
    }
}
