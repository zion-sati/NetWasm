// Portions derived from dotnet/runtime System.Linq.Expressions at commit
// deae2e32bf0e018e09d64597b7c59b62b73bb3ce.
// Licensed to the .NET Foundation under one or more agreements. The .NET
// Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Reflection;

namespace System.Linq.Expressions
{
    public class InvocationExpression : Expression, IArgumentProvider
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

        Expression IArgumentProvider.GetArgument(int index) => Arguments[index];

        int IArgumentProvider.ArgumentCount => Arguments.Count;

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
            LambdaExpression? lambda = expression as LambdaExpression;
            if (expression is UnaryExpression
                {
                    NodeType: ExpressionType.Quote,
                    Operand: LambdaExpression quotedLambda,
                })
            {
                delegateType = quotedLambda.Type;
                lambda = quotedLambda;
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
            // A lambda names its delegate type when compiling; another expression's delegate type
            // is known only at run time, without Invoke metadata.
            MethodInfo invoke = lambda?.InvokeMethod ??
                throw System.Runtime.CompilerServices.DelegateMetadata.UnknownDelegateType();
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
