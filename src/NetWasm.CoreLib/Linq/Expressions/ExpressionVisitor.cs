// Portions derived from dotnet/runtime System.Linq.Expressions at commit
// 811225a482702af7ecc35d817966bc70b88a3a23.
// Licensed to the .NET Foundation under one or more agreements. The .NET
// Foundation licenses this file to you under the MIT license.

using System.Collections.ObjectModel;

namespace System.Linq.Expressions
{
    public abstract class ExpressionVisitor
    {
        protected ExpressionVisitor()
        {
        }

        public virtual Expression? Visit(Expression? node) => node?.Accept(this);

        public ReadOnlyCollection<Expression> Visit(
            ReadOnlyCollection<Expression> nodes) =>
            Visit(nodes, node => Visit(node)!);

        public static ReadOnlyCollection<T> Visit<T>(
            ReadOnlyCollection<T> nodes,
            Func<T, T> elementVisitor)
        {
            ArgumentNullException.ThrowIfNull(nodes);
            ArgumentNullException.ThrowIfNull(elementVisitor);
            T[]? replacements = null;
            for (var index = 0; index < nodes.Count; index++)
            {
                T replacement = elementVisitor(nodes[index]);
                if (replacements is not null)
                {
                    replacements[index] = replacement;
                }
                else if (!ReferenceEquals(replacement, nodes[index]))
                {
                    replacements = new T[nodes.Count];
                    for (var previous = 0; previous < index; previous++)
                    {
                        replacements[previous] = nodes[previous];
                    }
                    replacements[index] = replacement;
                }
            }
            return replacements is null
                ? nodes
                : new ReadOnlyCollection<T>(replacements);
        }

        public T? VisitAndConvert<T>(T? node, string? callerName)
            where T : Expression
        {
            if (node is null)
            {
                return null;
            }
            if (Visit(node) is not T replacement)
            {
                throw new InvalidOperationException(
                    "A visitor must rewrite this node to the same expression type.");
            }
            return replacement;
        }

        public ReadOnlyCollection<T> VisitAndConvert<T>(
            ReadOnlyCollection<T> nodes,
            string? callerName)
            where T : Expression =>
            Visit(nodes, node => VisitAndConvert(node, callerName) ??
                throw new InvalidOperationException(
                    "A visitor cannot replace a required expression with null."));

        protected internal virtual Expression VisitBinary(BinaryExpression node)
        {
            Expression? left = Visit(node.Left);
            LambdaExpression? conversion = VisitAndConvert(
                node.Conversion,
                nameof(VisitBinary));
            Expression? right = Visit(node.Right);
            BinaryExpression replacement = node.Update(
                left!,
                conversion,
                right!);
            if (replacement != node && node.Method is null)
            {
                if (replacement.Method is not null)
                {
                    throw new InvalidOperationException(
                        "A visitor cannot add an operator method while rewriting a built-in binary expression.");
                }
                ValidateChildType(node.Left.Type, replacement.Left.Type);
                ValidateChildType(node.Right.Type, replacement.Right.Type);
            }
            return replacement;
        }

        protected internal virtual Expression VisitBlock(BlockExpression node)
        {
            ReadOnlyCollection<Expression> expressions = Visit(node.Expressions);
            ReadOnlyCollection<ParameterExpression> variables = VisitAndConvert(
                node.Variables,
                nameof(VisitBlock));
            return node.Update(variables, expressions);
        }

        protected internal virtual Expression VisitConditional(
            ConditionalExpression node) =>
            node.Update(
                Visit(node.Test)!,
                Visit(node.IfTrue)!,
                Visit(node.IfFalse)!);

        protected internal virtual Expression VisitConstant(ConstantExpression node) =>
            node;

        protected internal virtual Expression VisitDefault(DefaultExpression node) =>
            node;

        protected internal virtual Expression VisitExtension(Expression node) =>
            node.VisitChildren(this);

        protected internal virtual Expression VisitInvocation(
            InvocationExpression node) =>
            node.Update(
                Visit(node.Expression)!,
                Visit(node.Arguments));

        protected internal virtual Expression VisitLambda<TDelegate>(
            Expression<TDelegate> node) =>
            node.Update(
                Visit(node.Body)!,
                VisitAndConvert(node.Parameters, nameof(VisitLambda)));

        protected internal virtual Expression VisitMember(MemberExpression node) =>
            node.Update(Visit(node.Expression));

        protected internal virtual Expression VisitMethodCall(
            MethodCallExpression node) =>
            node.Update(
                Visit(node.Object),
                Visit(node.Arguments));

        protected internal virtual Expression VisitNew(NewExpression node) =>
            node.Update(Visit(node.Arguments));

        protected internal virtual Expression VisitParameter(ParameterExpression node) =>
            node;

        protected internal virtual Expression VisitUnary(UnaryExpression node)
        {
            Expression? operand = Visit(node.Operand);
            UnaryExpression replacement = node.Update(operand!);
            if (replacement != node && node.Method is null)
            {
                if (replacement.Method is not null)
                {
                    throw new InvalidOperationException(
                        "A visitor cannot add an operator method while rewriting a built-in unary expression.");
                }
                ValidateChildType(node.Operand.Type, replacement.Operand.Type);
            }
            return replacement;
        }

        private static void ValidateChildType(Type before, Type after)
        {
            if (before.IsValueType)
            {
                if (before == after)
                {
                    return;
                }
            }
            else if (!after.IsValueType)
            {
                return;
            }
            throw new InvalidOperationException(
                "A visitor must preserve value-type children when rewriting an operator.");
        }
    }
}
