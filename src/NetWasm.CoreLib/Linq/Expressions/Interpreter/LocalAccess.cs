// Portions derived from dotnet/runtime System.Linq.Expressions at commit
// 811225a482702af7ecc35d817966bc70b88a3a23.
// Licensed to the .NET Foundation under one or more agreements. The .NET
// Foundation licenses this file to you under the MIT license.

namespace System.Linq.Expressions.Interpreter
{
    internal abstract class LocalAccessInstruction(int index) : Instruction
    {
        protected int Index { get; } = index;
    }

    internal sealed class LoadLocalInstruction(int index) :
        LocalAccessInstruction(index)
    {
        internal override int ProducedStack => 1;

        internal override int Run(InterpretedFrame frame)
        {
            frame.Push(frame.Data[Index]);
            return 1;
        }
    }

    internal sealed class AssignLocalInstruction(int index) :
        LocalAccessInstruction(index)
    {
        internal override int ConsumedStack => 1;

        internal override int ProducedStack => 1;

        internal override int Run(InterpretedFrame frame)
        {
            frame.Data[Index] = frame.Peek();
            return 1;
        }
    }

    internal sealed class StoreLocalInstruction(int index) :
        LocalAccessInstruction(index)
    {
        internal override int ConsumedStack => 1;

        internal override int Run(InterpretedFrame frame)
        {
            frame.Data[Index] = frame.Pop();
            return 1;
        }
    }

    internal sealed class InitializeLocalInstruction(
        int index,
        object? defaultValue) : LocalAccessInstruction(index)
    {
        internal override int Run(InterpretedFrame frame)
        {
            frame.Data[Index] = defaultValue;
            return 1;
        }
    }
}
