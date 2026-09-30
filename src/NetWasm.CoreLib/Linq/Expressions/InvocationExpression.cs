// Portions derived from dotnet/runtime System.Linq.Expressions at commit
// 811225a482702af7ecc35d817966bc70b88a3a23.
// Licensed to the .NET Foundation under one or more agreements. The .NET
// Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Reflection;

namespace System.Linq.Expressions
{
    public class InvocationExpression : Expression
    {
        private readonly Type _type;

        internal InvocationExpression(
            Expression expression,
            Expression[] arguments,
            Type type)
        {
            Expression = expression;
            Arguments = arguments.Length == 0
                ? ReadOnlyCollection<Expression>.Empty
                : new ReadOnlyCollection<Expression>(arguments);
            _type = type;
        }

        public sealed override ExpressionType NodeType => ExpressionType.Invoke;

        public override Type Type => _type;

        public Expression Expression { get; }

        public ReadOnlyCollection<Expression> Arguments { get; }

        public InvocationExpression Update(
            Expression expression,
            IEnumerable<Expression>? arguments)
        {
            Expression[] argumentArray = ExpressionValidation.CopyExpressions(
                arguments,
                nameof(arguments));
            return ReferenceEquals(expression, Expression) &&
                ExpressionValidation.SameExpressions(
                    Arguments,
                    argumentArray)
                ? this
                : Invoke(expression, argumentArray);
        }

        protected internal override Expression Accept(ExpressionVisitor visitor) =>
            visitor.VisitInvocation(this);
    }

    public abstract partial class Expression
    {
        public static InvocationExpression Invoke(
            Expression expression,
            params Expression[]? arguments) =>
            Invoke(expression, (IEnumerable<Expression>?)arguments);

        public static InvocationExpression Invoke(
            Expression expression,
            IEnumerable<Expression>? arguments)
        {
            ArgumentNullException.ThrowIfNull(expression);
            ExpressionValidation.RequiresCanRead(
                expression,
                nameof(expression));
            Type delegateType;
            if (expression is UnaryExpression
                {
                    NodeType: ExpressionType.Quote,
                    Operand: LambdaExpression quotedLambda,
                })
            {
                delegateType = quotedLambda.Type;
            }
            else
            {
                delegateType = expression.Type;
                if (typeof(LambdaExpression).IsAssignableFrom(delegateType))
                {
                    throw new NotSupportedException(
                        "Invoking an expression-tree value without an explicit quote " +
                        "is outside the NetWasm profile.");
                }
            }
            if (delegateType == typeof(MulticastDelegate) ||
                !typeof(MulticastDelegate).IsAssignableFrom(delegateType))
            {
                throw new ArgumentException(
                    "The invoked expression must have a concrete delegate type.",
                    nameof(expression));
            }
            MethodInfo invoke = delegateType.GetDelegateInvokeMethod();
            Expression[] argumentArray = ExpressionValidation.CopyExpressions(
                arguments,
                nameof(arguments));
            ExpressionValidation.ValidateArguments(
                invoke,
                argumentArray,
                nameof(arguments));
            return new InvocationExpression(
                expression,
                argumentArray,
                invoke.ReturnType);
        }
    }
}
