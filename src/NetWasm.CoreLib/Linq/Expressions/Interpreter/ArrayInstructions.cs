// Portions derived from dotnet/runtime System.Linq.Expressions at commit
// deae2e32bf0e018e09d64597b7c59b62b73bb3ce (Interpreter/ArrayOperations.cs, NewArrayInstruction).
// Licensed to the .NET Foundation under one or more agreements. The .NET
// Foundation licenses this file to you under the MIT license.
// NetWasm adaptation: elements are read, written and created through Array's GetValue, SetValue and CreateInstance,
// which the compiler lowers for the element types a program names, so every element is held boxed as the
// interpreter holds every value. Only one-dimensional arrays are created: the rectangular CreateInstance overloads
// make the compiler root rank 2 and 3 arrays of every element type in the program, which every program compiling an
// expression would pay for.

namespace System.Linq.Expressions.Interpreter
{
    internal static class ArrayIndexes
    {
        // An index or a length of any integral type, as the expression factories admit, checked into an int.
        internal static int ToInt32(object? value) => value switch
        {
            int index => index,
            long index => checked((int)index),
            short index => index,
            sbyte index => index,
            byte index => index,
            ushort index => index,
            uint index => checked((int)index),
            ulong index => checked((int)index),
            null => throw new NullReferenceException(),
            _ => throw new InvalidCastException(),
        };

        internal static int[] Pop(InterpretedFrame frame, int count)
        {
            var indexes = new int[count];
            for (var index = count - 1; index >= 0; index--)
            {
                indexes[index] = ToInt32(frame.Pop());
            }
            return indexes;
        }
    }

    // Reads an element: the array, then one index per rank.
    internal sealed class ArrayGetInstruction(int indexCount) : Instruction
    {
        private readonly int _indexCount = indexCount;

        internal override int ConsumedStack => _indexCount + 1;

        internal override int ProducedStack => 1;

        internal override int Run(InterpretedFrame frame)
        {
            var indexes = ArrayIndexes.Pop(frame, _indexCount);
            var array = (Array)frame.Pop()!;
            frame.Push(_indexCount == 1 ? array.GetValue(indexes[0]) : array.GetValue(indexes));
            return 1;
        }
    }

    // Writes an element: the array, one index per rank, then the value, which an assignment used as a value keeps.
    internal sealed class ArraySetInstruction(int indexCount, bool preservesValue) : Instruction
    {
        private readonly int _indexCount = indexCount;
        private readonly bool _preservesValue = preservesValue;

        internal override int ConsumedStack => _indexCount + 2;

        internal override int ProducedStack => _preservesValue ? 1 : 0;

        internal override int Run(InterpretedFrame frame)
        {
            var value = frame.Pop();
            var indexes = ArrayIndexes.Pop(frame, _indexCount);
            var array = (Array)frame.Pop()!;
            if (_indexCount == 1)
            {
                array.SetValue(value, indexes[0]);
            }
            else
            {
                array.SetValue(value, indexes);
            }
            if (_preservesValue)
            {
                frame.Push(value);
            }
            return 1;
        }
    }

    // NewArrayInit: a one-dimensional array of the elements on the stack, in order.
    internal sealed class NewArrayInitInstruction(Type elementType, int count) : Instruction
    {
        private readonly Type _elementType = elementType;
        private readonly int _count = count;

        internal override int ConsumedStack => _count;

        internal override int ProducedStack => 1;

        internal override int Run(InterpretedFrame frame)
        {
            var array = Array.CreateInstance(_elementType, _count);
            for (var index = _count - 1; index >= 0; index--)
            {
                array.SetValue(frame.Pop(), index);
            }
            frame.Push(array);
            return 1;
        }
    }

    // NewArrayBounds: an array with one length per rank. A negative length throws OverflowException, as newarr does.
    // A one-dimensional array of the given length.
    internal sealed class NewArrayBoundsInstruction(Type elementType) : Instruction
    {
        private readonly Type _elementType = elementType;

        internal override int ConsumedStack => 1;

        internal override int ProducedStack => 1;

        internal override int Run(InterpretedFrame frame)
        {
            var length = ArrayIndexes.ToInt32(frame.Pop());
            if (length < 0)
            {
                throw new OverflowException();
            }
            frame.Push(Array.CreateInstance(_elementType, length));
            return 1;
        }
    }
}
