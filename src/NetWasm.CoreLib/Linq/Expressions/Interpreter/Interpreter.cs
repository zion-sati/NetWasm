// Portions derived from dotnet/runtime System.Linq.Expressions at commit
// deae2e32bf0e018e09d64597b7c59b62b73bb3ce.
// Licensed to the .NET Foundation under one or more agreements. The .NET
// Foundation licenses this file to you under the MIT license.

namespace System.Linq.Expressions.Interpreter
{
    internal sealed class Interpreter
    {
        private readonly InstructionArray _instructions;
        private readonly int _localCount;
        private readonly bool _returnsValue;
        private readonly TryRegion[] _regions;

        // The try regions, innermost first, so an exception meets the nearest handler.
        internal Interpreter(InstructionArray instructions, int localCount, bool returnsValue, TryRegion[] regions)
        {
            _instructions = instructions;
            _localCount = localCount;
            _returnsValue = returnsValue;
            _regions = regions;
            Array.Sort(_regions, static (left, right) =>
                (left.End - left.TryStart).CompareTo(right.End - right.TryStart));
        }

        internal object? Run(object?[] arguments, System.Runtime.CompilerServices.StrongBox<object?>[]? closure)
        {
            var frame = new InterpretedFrame(
                arguments,
                _localCount,
                _instructions.MaxStackDepth)
            {
                Closure = closure,
            };
            var instructions = _instructions.Instructions;
            var index = 0;
            while (index < instructions.Length)
            {
                frame.InstructionIndex = index;
                int delta;
                try
                {
                    delta = instructions[index].Run(frame);
                }
                catch (Exception exception)
                {
                    var target = Handle(frame, index, exception);
                    if (target < 0)
                    {
                        throw;
                    }
                    index = target;
                    continue;
                }
                if (delta == 0 || index + delta < 0 ||
                    index + delta > instructions.Length)
                {
                    throw new InvalidOperationException(
                        "The interpreted instruction produced an invalid branch target.");
                }
                index += delta;
            }

            // A lambda with a result leaves it on the stack; a void lambda leaves nothing.
            if (frame.StackIndex != _localCount + (_returnsValue ? 1 : 0))
            {
                throw new InvalidOperationException(
                    "The interpreted lambda completed with an invalid stack depth.");
            }
            return _returnsValue ? frame.Pop() : null;
        }

        private int Handle(InterpretedFrame frame, int index, Exception exception)
        {
            foreach (var region in _regions)
            {
                if (region.TryStart <= index && index < region.End)
                {
                    var target = region.Handle(frame, _localCount, index, exception);
                    if (target >= 0)
                    {
                        return target;
                    }
                }
            }
            return -1;
        }
    }
}
