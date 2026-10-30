// Portions derived from dotnet/runtime System.Linq.Expressions at commit
// 474ed963699fc29809015392738a15231aaae2de.
// Licensed to the .NET Foundation under one or more agreements. The .NET
// Foundation licenses this file to you under the MIT license.

namespace System.Linq.Expressions.Interpreter
{
    internal abstract class Instruction
    {
        internal virtual int ConsumedStack => 0;

        internal virtual int ProducedStack => 0;

        internal int StackBalance => ProducedStack - ConsumedStack;

        internal abstract int Run(InterpretedFrame frame);
    }

    internal readonly struct InstructionArray
    {
        internal InstructionArray(Instruction[] instructions, int maxStackDepth)
        {
            Instructions = instructions;
            MaxStackDepth = maxStackDepth;
        }

        internal Instruction[] Instructions { get; }

        internal int MaxStackDepth { get; }
    }
}
