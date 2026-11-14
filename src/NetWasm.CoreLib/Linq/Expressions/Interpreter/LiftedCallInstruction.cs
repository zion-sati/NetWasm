// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// NetWasm addition: lifted user-defined operators, with the results .NET gives a lifted operator a null operand.

namespace System.Linq.Expressions.Interpreter
{
    // What a lifted operator gives when an operand is null: null for a result lifted to null; false for an ordering
    // comparison; for == and !=, whether both or one are null; and for a conversion to a value type,
    // InvalidOperationException, as reading a null Nullable<T>'s Value does.
    internal enum LiftedNullResult
    {
        Null,
        False,
        EqualWhenBothNull,
        UnequalWhenOneNull,
        Throw,
    }

    // Decorates the operator's call: it runs only when no operand is null, as the interpreter holds a nullable as its
    // value's box or null.
    internal sealed class LiftedCallInstruction(CallInstruction call, LiftedNullResult nullResult) : Instruction
    {
        internal override int ConsumedStack => call.ConsumedStack;

        internal override int ProducedStack => 1;

        internal static LiftedNullResult NullResultOf(Expression node) =>
            node.Type.IsNullable ? LiftedNullResult.Null : node.NodeType switch
            {
                ExpressionType.Equal => LiftedNullResult.EqualWhenBothNull,
                ExpressionType.NotEqual => LiftedNullResult.UnequalWhenOneNull,
                ExpressionType.LessThan or ExpressionType.LessThanOrEqual or
                    ExpressionType.GreaterThan or ExpressionType.GreaterThanOrEqual => LiftedNullResult.False,
                _ => LiftedNullResult.Throw,
            };

        internal override int Run(InterpretedFrame frame)
        {
            var first = frame.StackIndex - ConsumedStack;
            var nulls = 0;
            for (var index = first; index < frame.StackIndex; index++)
            {
                if (frame.Data[index] is null)
                {
                    nulls++;
                }
            }
            if (nulls == 0)
            {
                return call.Run(frame);
            }
            object? result = nullResult switch
            {
                LiftedNullResult.Null => null,
                LiftedNullResult.False => false,
                LiftedNullResult.EqualWhenBothNull => nulls == ConsumedStack,
                LiftedNullResult.UnequalWhenOneNull => nulls != ConsumedStack,
                _ => throw new InvalidOperationException(global::System.SR.InvalidOperation_NoValue),
            };
            while (frame.StackIndex > first)
            {
                frame.Pop();
            }
            frame.Push(result);
            return 1;
        }
    }
}
