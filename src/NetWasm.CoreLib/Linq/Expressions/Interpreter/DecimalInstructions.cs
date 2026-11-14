// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// NetWasm adaptation: upstream's interpreter calls decimal's operator methods through reflection; NetWasm runs
// decimal's operators directly (DecimalOperators). The interpreter holds a nullable as null or its boxed value, and
// lifted operators follow upstream's rules: arithmetic and lifted-to-null comparisons with a null operand yield null,
// other comparisons false, and lifted equality compares nulls as equal.

namespace System.Linq.Expressions.Interpreter
{
    internal sealed class DecimalBinaryInstruction(DecimalOperatorKind kind, bool liftedToNull) : Instruction
    {
        private readonly DecimalOperatorKind _kind = kind;
        private readonly bool _liftedToNull = liftedToNull;

        internal override int ConsumedStack => 2;

        internal override int ProducedStack => 1;

        internal override int Run(InterpretedFrame frame)
        {
            var right = frame.Pop();
            var left = frame.Pop();
            if (left is null || right is null)
            {
                frame.Push(_kind switch
                {
                    _ when _liftedToNull => null,
                    DecimalOperatorKind.Equal => left is null && right is null,
                    DecimalOperatorKind.NotEqual => (left is null) != (right is null),
                    DecimalOperatorKind.LessThan or DecimalOperatorKind.LessThanOrEqual or
                        DecimalOperatorKind.GreaterThan or DecimalOperatorKind.GreaterThanOrEqual => false,
                    _ => null,
                });
                return 1;
            }
            frame.Push(DecimalOperators.Apply(_kind, (decimal)left, (decimal)right));
            return 1;
        }
    }

    internal sealed class DecimalUnaryInstruction(DecimalOperatorKind kind) : Instruction
    {
        private readonly DecimalOperatorKind _kind = kind;

        internal override int ConsumedStack => 1;

        internal override int ProducedStack => 1;

        internal override int Run(InterpretedFrame frame)
        {
            var operand = frame.Pop();
            frame.Push(operand is null ? null : DecimalOperators.Apply(_kind, (decimal)operand));
            return 1;
        }
    }

    // A conversion to or from decimal; a null source converts to null for a nullable result and throws otherwise.
    internal sealed class DecimalConvertInstruction(TypeCode from, TypeCode to, bool resultNullable) : Instruction
    {
        private readonly TypeCode _from = from;
        private readonly TypeCode _to = to;
        private readonly bool _resultNullable = resultNullable;

        internal override int ConsumedStack => 1;

        internal override int ProducedStack => 1;

        internal override int Run(InterpretedFrame frame)
        {
            var value = frame.Pop();
            if (value is null)
            {
                if (!_resultNullable)
                {
                    throw new InvalidOperationException("Nullable object must have a value.");
                }
                frame.Push(null);
                return 1;
            }
            frame.Push(_from == _to ? value : DecimalOperators.Convert(value, _from, _to));
            return 1;
        }
    }
}
