// Portions derived from dotnet/runtime System.Linq.Expressions at commit
// deae2e32bf0e018e09d64597b7c59b62b73bb3ce.
// Licensed to the .NET Foundation under one or more agreements. The .NET
// Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Dynamic.Utils;
using System.Reflection;

namespace System.Linq.Expressions
{
    public class MethodCallExpression : Expression, IArgumentProvider
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

        Expression IArgumentProvider.GetArgument(int index) => Arguments[index];

        int IArgumentProvider.ArgumentCount => Arguments.Count;

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
        public static MethodCallExpression Call(MethodInfo method, Expression arg0) =>
            Call(null, method, new[] { arg0 });

        public static MethodCallExpression Call(
            MethodInfo method,
            Expression arg0,
            Expression arg1) =>
            Call(null, method, new[] { arg0, arg1 });

        public static MethodCallExpression Call(
            MethodInfo method,
            Expression arg0,
            Expression arg1,
            Expression arg2) =>
            Call(null, method, new[] { arg0, arg1, arg2 });

        public static MethodCallExpression Call(
            MethodInfo method,
            Expression arg0,
            Expression arg1,
            Expression arg2,
            Expression arg3) =>
            Call(null, method, new[] { arg0, arg1, arg2, arg3 });

        public static MethodCallExpression Call(
            MethodInfo method,
            Expression arg0,
            Expression arg1,
            Expression arg2,
            Expression arg3,
            Expression arg4) =>
            Call(null, method, new[] { arg0, arg1, arg2, arg3, arg4 });

        public static MethodCallExpression Call(
            MethodInfo method,
            params Expression[]? arguments) =>
            Call(null, method, arguments);

        public static MethodCallExpression Call(Expression? instance, MethodInfo method) =>
            Call(instance, method, (IEnumerable<Expression>?)null);

        public static MethodCallExpression Call(
            Expression? instance,
            MethodInfo method,
            Expression arg0,
            Expression arg1) =>
            Call(instance, method, new[] { arg0, arg1 });

        public static MethodCallExpression Call(
            Expression? instance,
            MethodInfo method,
            Expression arg0,
            Expression arg1,
            Expression arg2) =>
            Call(instance, method, new[] { arg0, arg1, arg2 });

        public static MethodCallExpression Call(
            MethodInfo method,
            IEnumerable<Expression>? arguments) =>
            Call(null, method, arguments);

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

        private static void ValidateCallInstanceType(Type instanceType, MethodInfo method)
        {
            if (!TypeUtils.IsValidInstanceType(method, instanceType))
            {
                throw Error.InstanceAndMethodTypeMismatch(method, method.DeclaringType, instanceType);
            }
        }

        private static void ValidateArgumentTypes(MethodBase method, ExpressionType nodeKind, ref ReadOnlyCollection<Expression> arguments, string methodParamName)
        {
            ExpressionUtils.ValidateArgumentTypes(method, nodeKind, ref arguments, methodParamName);
        }

        public static MethodCallExpression ArrayIndex(Expression array, params Expression[] indexes)
        {
            return ArrayIndex(array, (IEnumerable<Expression>)indexes);
        }

        public static MethodCallExpression ArrayIndex(Expression array, IEnumerable<Expression> indexes)
        {
            ExpressionUtils.RequiresCanRead(array, nameof(array), -1);
            ArgumentNullException.ThrowIfNull(indexes);

            Type arrayType = array.Type;
            if (!arrayType.IsArray)
            {
                throw Error.ArgumentMustBeArray(nameof(array));
            }

            ReadOnlyCollection<Expression> indexList = indexes.ToReadOnly();
            if (arrayType.GetArrayRank() != indexList.Count)
            {
                throw Error.IncorrectNumberOfIndexes();
            }

            for (int i = 0, n = indexList.Count; i < n; i++)
            {
                Expression e = indexList[i];

                ExpressionUtils.RequiresCanRead(e, nameof(indexes), i);
                if (e.Type != typeof(int))
                {
                    throw Error.ArgumentMustBeArrayIndexType(nameof(indexes), i);
                }
            }

            MethodInfo mi = TypeUtils.GetArrayGetMethod(array.Type);
            return Call(array, mi, indexList);
        }
    }
}
