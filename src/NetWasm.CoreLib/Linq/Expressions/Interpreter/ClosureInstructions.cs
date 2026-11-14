// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Portions derived from dotnet/runtime System.Linq.Expressions at commit
// deae2e32bf0e018e09d64597b7c59b62b73bb3ce (LocalAccess.cs, TypeOperations.cs: CreateDelegateInstruction and
// QuoteInstruction). NetWasm adaptation: a captured variable's local holds a StrongBox<object?> from its definition
// (ClosureVariableFinder), a nested lambda's delegate is its Expression<TDelegate>'s own planned adapter over a
// LightLambda that carries the closure, and a quote reads a captured variable through StrongBox<object?>.Value,
// named by a token.

using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace System.Linq.Expressions.Interpreter
{
    // A captured parameter: the argument the frame seeded becomes the box its closures share.
    internal sealed class BoxLocalInstruction(int index) : LocalAccessInstruction(index)
    {
        internal override int Run(InterpretedFrame frame)
        {
            frame.Data[Index] = new StrongBox<object?>(frame.Data[Index]);
            return 1;
        }
    }

    // A captured catch variable: the exception the handler took (whose own local rethrow reads) in a new box.
    internal sealed class CopyToBoxedLocalInstruction(int source, int index) : LocalAccessInstruction(index)
    {
        internal override int Run(InterpretedFrame frame)
        {
            frame.Data[Index] = new StrongBox<object?>(frame.Data[source]);
            return 1;
        }
    }

    // A captured block variable: a new box on each entry to its block, so each closure made in a loop keeps its own.
    internal sealed class InitializeBoxedLocalInstruction(int index, object? defaultValue) : LocalAccessInstruction(index)
    {
        internal override int Run(InterpretedFrame frame)
        {
            frame.Data[Index] = new StrongBox<object?>(defaultValue);
            return 1;
        }
    }

    internal sealed class LoadBoxedLocalInstruction(int index) : LocalAccessInstruction(index)
    {
        internal override int ProducedStack => 1;

        internal override int Run(InterpretedFrame frame)
        {
            frame.Push(((StrongBox<object?>)frame.Data[Index]!).Value);
            return 1;
        }
    }

    // Stores the value on the stack, keeping it there when the assignment's value is used.
    internal sealed class StoreBoxedLocalInstruction(int index, bool preservesValue) : LocalAccessInstruction(index)
    {
        internal override int ConsumedStack => 1;

        internal override int ProducedStack => preservesValue ? 1 : 0;

        internal override int Run(InterpretedFrame frame)
        {
            ((StrongBox<object?>)frame.Data[Index]!).Value = preservesValue ? frame.Peek() : frame.Pop();
            return 1;
        }
    }

    internal sealed class LoadClosureInstruction(int index) : Instruction
    {
        internal override int ProducedStack => 1;

        internal override int Run(InterpretedFrame frame)
        {
            frame.Push(frame.Closure![index].Value);
            return 1;
        }
    }

    internal sealed class StoreClosureInstruction(int index, bool preservesValue) : Instruction
    {
        internal override int ConsumedStack => 1;

        internal override int ProducedStack => preservesValue ? 1 : 0;

        internal override int Run(InterpretedFrame frame)
        {
            frame.Closure![index].Value = preservesValue ? frame.Peek() : frame.Pop();
            return 1;
        }
    }

    // The box itself, for a nested lambda's closure or a quote.
    internal sealed class LoadClosureBoxInstruction(int index) : Instruction
    {
        internal override int ProducedStack => 1;

        internal override int Run(InterpretedFrame frame)
        {
            frame.Push(frame.Closure![index]);
            return 1;
        }
    }

    // A nested lambda: the boxes of the variables it captures, in its closure's order, make its delegate.
    internal sealed class CreateDelegateInstruction(
        LambdaExpression lambda,
        Interpreter interpreter,
        int closureSize) : Instruction
    {
        internal override int ConsumedStack => closureSize;

        internal override int ProducedStack => 1;

        internal override int Run(InterpretedFrame frame)
        {
            StrongBox<object?>[]? closure = null;
            if (closureSize > 0)
            {
                closure = new StrongBox<object?>[closureSize];
                for (var index = closureSize - 1; index >= 0; index--)
                {
                    closure[index] = (StrongBox<object?>)frame.Pop()!;
                }
            }
            frame.Push(lambda.CreateInterpretedDelegate(
                new LightLambda(interpreter, lambda.Parameters.Count, closure)));
            return 1;
        }
    }

    // Where a quoted expression's captured variable lives: a boxed local of the quoting frame, or its closure.
    internal readonly record struct QuotedVariable(bool InClosure, int Index);

    // Quote: the expression itself, with each captured variable read through its box, as C# quoting does.
    internal sealed class QuoteInstruction(
        Expression operand,
        Dictionary<ParameterExpression, QuotedVariable>? captured) : Instruction
    {
        internal override int ProducedStack => 1;

        internal override int Run(InterpretedFrame frame)
        {
            frame.Push(captured is null ? operand : new ExpressionQuoter(captured, frame).Visit(operand));
            return 1;
        }

        // Rewrites each captured variable a quoted expression names, where no scope inside it shadows the variable,
        // into a read of its box's value.
        private sealed class ExpressionQuoter(
            Dictionary<ParameterExpression, QuotedVariable> variables,
            InterpretedFrame frame) : ExpressionVisitor
        {
            // StrongBox<object?>.Value, named by the token C# builds this lambda with rather than looked up by name.
            private static readonly FieldInfo s_boxValue = (FieldInfo)((MemberExpression)
                ((Expression<Func<StrongBox<object?>, object?>>)(box => box.Value)).Body).Member;

            private readonly List<IReadOnlyCollection<ParameterExpression>> _shadowed = new();

            protected internal override Expression VisitLambda<TDelegate>(Expression<TDelegate> node)
            {
                _shadowed.Add(node.Parameters);
                try
                {
                    var body = Visit(node.Body)!;
                    return body == node.Body ? node : node.Update(body, node.Parameters);
                }
                finally
                {
                    _shadowed.RemoveAt(_shadowed.Count - 1);
                }
            }

            protected internal override Expression VisitBlock(BlockExpression node)
            {
                _shadowed.Add(node.Variables);
                try
                {
                    return base.VisitBlock(node);
                }
                finally
                {
                    _shadowed.RemoveAt(_shadowed.Count - 1);
                }
            }

            protected override CatchBlock VisitCatchBlock(CatchBlock node)
            {
                _shadowed.Add(node.Variable is null ? Array.Empty<ParameterExpression>() : new[] { node.Variable });
                try
                {
                    return base.VisitCatchBlock(node);
                }
                finally
                {
                    _shadowed.RemoveAt(_shadowed.Count - 1);
                }
            }

            protected internal override Expression VisitParameter(ParameterExpression node)
            {
                foreach (var scope in _shadowed)
                {
                    foreach (var defined in scope)
                    {
                        if (defined == node)
                        {
                            return node;
                        }
                    }
                }
                if (!variables.TryGetValue(node, out var variable))
                {
                    return node;
                }
                var box = variable.InClosure
                    ? frame.Closure![variable.Index]
                    : (StrongBox<object?>)frame.Data[variable.Index]!;
                return Expression.Convert(Expression.Field(Expression.Constant(box), s_boxValue), node.Type);
            }
        }
    }
}
