// Portions derived from dotnet/runtime System.Linq.Expressions at commit
// 474ed963699fc29809015392738a15231aaae2de.
// Licensed to the .NET Foundation under one or more agreements. The .NET
// Foundation licenses this file to you under the MIT license.

namespace System.Linq.Expressions.Interpreter
{
    internal sealed class InterpretedFrame
    {
        internal InterpretedFrame(
            object?[] arguments,
            int localCount,
            int maxStackDepth)
        {
            if (arguments.Length > localCount)
            {
                throw new InvalidOperationException(
                    "The interpreted frame has fewer locals than arguments.");
            }

            Data = new object?[checked(localCount + maxStackDepth)];
            for (var index = 0; index < arguments.Length; index++)
            {
                Data[index] = arguments[index];
            }
            StackIndex = localCount;
        }

        internal object?[] Data { get; }

        internal int StackIndex { get; set; }

        internal int InstructionIndex { get; set; }

        internal void Push(object? value) => Data[StackIndex++] = value;

        internal object? Pop()
        {
            var index = --StackIndex;
            var value = Data[index];
            Data[index] = null;
            return value;
        }

        internal object? Peek() => Data[StackIndex - 1];
    }
}
