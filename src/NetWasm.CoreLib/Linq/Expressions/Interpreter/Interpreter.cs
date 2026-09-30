// Portions derived from dotnet/runtime System.Linq.Expressions at commit
// 811225a482702af7ecc35d817966bc70b88a3a23.
// Licensed to the .NET Foundation under one or more agreements. The .NET
// Foundation licenses this file to you under the MIT license.

namespace System.Linq.Expressions.Interpreter
{
    internal sealed class Interpreter
    {
        private readonly InstructionArray _instructions;
        private readonly int _localCount;

        internal Interpreter(InstructionArray instructions, int localCount)
        {
            _instructions = instructions;
            _localCount = localCount;
        }

        internal object? Run(object?[] arguments)
        {
            var frame = new InterpretedFrame(
                arguments,
                _localCount,
                _instructions.MaxStackDepth);
            var instructions = _instructions.Instructions;
            var index = 0;
            while (index < instructions.Length)
            {
                frame.InstructionIndex = index;
                var delta = instructions[index].Run(frame);
                if (delta == 0 || index + delta < 0 ||
                    index + delta > instructions.Length)
                {
                    throw new InvalidOperationException(
                        "The interpreted instruction produced an invalid branch target.");
                }
                index += delta;
            }

            if (frame.StackIndex != _localCount + 1)
            {
                throw new InvalidOperationException(
                    "The interpreted lambda completed with an invalid stack depth.");
            }
            return frame.Pop();
        }
    }
}
