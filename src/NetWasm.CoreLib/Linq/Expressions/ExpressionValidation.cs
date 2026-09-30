// Portions derived from dotnet/runtime System.Linq.Expressions at commit
// 811225a482702af7ecc35d817966bc70b88a3a23.
// Licensed to the .NET Foundation under one or more agreements. The .NET
// Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Reflection;

namespace System.Linq.Expressions
{
    internal static class ExpressionValidation
    {
        internal static Expression[] CopyExpressions(
            IEnumerable<Expression>? expressions,
            string parameterName)
        {
            if (expressions is null)
            {
                return [];
            }
            var copy = new List<Expression>();
            foreach (Expression? expression in expressions)
            {
                if (expression is null)
                {
                    throw new ArgumentNullException(parameterName);
                }
                copy.Add(expression);
            }
            return copy.ToArray();
        }

        internal static bool AreReferenceAssignable(Type destination, Type source) =>
            destination == source ||
            !destination.IsValueType &&
            !source.IsValueType &&
            destination.IsAssignableFrom(source);

        internal static void RequiresCanRead(
            Expression expression,
            string parameterName)
        {
            ArgumentNullException.ThrowIfNull(expression, parameterName);
            if (expression is MemberExpression member &&
                member.Member is PropertyInfo property &&
                !property.CanRead)
            {
                throw new ArgumentException(
                    "The expression refers to a property without a getter.",
                    parameterName);
            }
        }

        internal static void ValidateType(
            Type type,
            string parameterName,
            bool allowVoid)
        {
            ArgumentNullException.ThrowIfNull(type, parameterName);
            if (!allowVoid && type == typeof(void))
            {
                throw new ArgumentException(
                    "The type cannot be void.",
                    parameterName);
            }
            if (type.ContainsGenericParameters)
            {
                throw new ArgumentException(
                    "Open generic types are not valid expression types.",
                    parameterName);
            }
        }

        internal static void ValidateInstance(
            Expression? instance,
            MethodBase method,
            string parameterName)
        {
            Type declaringType = method.DeclaringType ??
                throw new InvalidOperationException(
                    "The compiler-generated member descriptor has no declaring type.");
            if (method.IsStatic)
            {
                if (instance is not null)
                {
                    throw new ArgumentException(
                        "A static member cannot have an instance expression.",
                        parameterName);
                }
                return;
            }
            if (instance is null)
            {
                throw new ArgumentException(
                    "An instance member requires an instance expression.",
                    parameterName);
            }
            RequiresCanRead(instance, parameterName);
            if (!declaringType.IsAssignableFrom(instance.Type))
            {
                throw new ArgumentException(
                    "The instance expression does not match the declaring type.",
                    parameterName);
            }
        }

        internal static void ValidateArguments(
            MethodBase method,
            Expression[] arguments,
            string parameterName)
        {
            ParameterInfo[] parameters = method.GetParameters();
            if (parameters.Length != arguments.Length)
            {
                throw new ArgumentException(
                    "The argument count does not match the member signature.",
                    parameterName);
            }
            for (var index = 0; index < parameters.Length; index++)
            {
                RequiresCanRead(arguments[index], parameterName);
                Type parameterType = parameters[index].ParameterType;
                if (parameterType.IsByRef)
                {
                    throw new NotSupportedException(
                        "By-reference member arguments are not implemented.");
                }
                if (!AreReferenceAssignable(
                        parameterType,
                        arguments[index].Type) &&
                    !TryQuote(parameterType, ref arguments[index]))
                {
                    throw new ArgumentException(
                        "An argument expression does not match the member signature.",
                        parameterName);
                }
            }
        }

        internal static bool TryQuote(
            Type destination,
            ref Expression expression)
        {
            if (expression is LambdaExpression lambda &&
                (destination.SemanticTypeId ==
                    typeof(LambdaExpression).SemanticTypeId ||
                    destination.SemanticTypeId ==
                    lambda.PublicType.SemanticTypeId))
            {
                expression = Expression.Quote(expression);
                return true;
            }
            return false;
        }

        internal static bool SameExpressions(
            ReadOnlyCollection<Expression> current,
            IEnumerable<Expression>? expressions)
        {
            if (expressions is null)
            {
                return current.Count == 0;
            }
            using IEnumerator<Expression> enumerator = expressions.GetEnumerator();
            for (var index = 0; index < current.Count; index++)
            {
                if (!enumerator.MoveNext() ||
                    !ReferenceEquals(enumerator.Current, current[index]))
                {
                    return false;
                }
            }
            return !enumerator.MoveNext();
        }
    }
}
