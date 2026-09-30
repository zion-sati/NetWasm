// Portions derived from dotnet/runtime System.Linq.Expressions at commit
// 811225a482702af7ecc35d817966bc70b88a3a23.
// Licensed to the .NET Foundation under one or more agreements. The .NET
// Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Reflection;

namespace System.Linq.Expressions
{
    public class MethodCallExpression : Expression
    {
        internal MethodCallExpression(
            Expression? instance,
            MethodInfo method,
            Expression[] arguments)
        {
            Object = instance;
            Method = method;
            Arguments = arguments.Length == 0
                ? ReadOnlyCollection<Expression>.Empty
                : new ReadOnlyCollection<Expression>(arguments);
        }

        public sealed override ExpressionType NodeType => ExpressionType.Call;

        public override Type Type => Method.ReturnType;

        public Expression? Object { get; }

        public MethodInfo Method { get; }

        public ReadOnlyCollection<Expression> Arguments { get; }

        public MethodCallExpression Update(
            Expression? @object,
            IEnumerable<Expression>? arguments)
        {
            Expression[] argumentArray = ExpressionValidation.CopyExpressions(
                arguments,
                nameof(arguments));
            return ReferenceEquals(@object, Object) &&
                ExpressionValidation.SameExpressions(
                    Arguments,
                    argumentArray)
                ? this
                : Call(@object, Method, argumentArray);
        }

        protected internal override Expression Accept(ExpressionVisitor visitor) =>
            visitor.VisitMethodCall(this);
    }

    public abstract partial class Expression
    {
        public static MethodCallExpression Call(
            Expression? instance,
            MethodInfo method,
            params Expression[]? arguments) =>
            Call(instance, method, (IEnumerable<Expression>?)arguments);

        public static MethodCallExpression Call(
            Expression? instance,
            MethodInfo method,
            IEnumerable<Expression>? arguments)
        {
            ArgumentNullException.ThrowIfNull(method);
            if (method.ContainsGenericParameters)
            {
                throw new NotSupportedException(
                    "Open generic method calls are outside the NetWasm profile.");
            }
            ExpressionValidation.ValidateInstance(
                instance,
                method,
                nameof(instance));
            Expression[] argumentArray = ExpressionValidation.CopyExpressions(
                arguments,
                nameof(arguments));
            ExpressionValidation.ValidateArguments(
                method,
                argumentArray,
                nameof(arguments));
            return new MethodCallExpression(instance, method, argumentArray);
        }
    }
}
