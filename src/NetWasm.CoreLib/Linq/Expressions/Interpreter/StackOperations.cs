// Portions derived from dotnet/runtime System.Linq.Expressions at commit
// 811225a482702af7ecc35d817966bc70b88a3a23.
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
