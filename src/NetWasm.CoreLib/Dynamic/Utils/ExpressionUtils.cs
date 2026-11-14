// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Ported from dotnet/runtime commit deae2e32bf0e018e09d64597b7c59b62b73bb3ce.
// NetWasm adaptation: the subset the ported expression types use. TryQuote delegates to NetWasm's
// existing ExpressionValidation.TryQuote, so lambdas quote the same way through ported and existing
// factories; parameters are read with MethodBase.GetParameters().
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;

namespace System.Dynamic.Utils
{
    internal static class ExpressionUtils
    {
        public static ReadOnlyCollection<T> ReturnReadOnly<T>(ref IReadOnlyList<T> collection)
        {
            IReadOnlyList<T> value = collection;

            // if it's already read-only just return it.
            if (value is ReadOnlyCollection<T> res)
            {
                return res;
            }

            // otherwise make sure only read-only collection every gets exposed
            Interlocked.CompareExchange(ref collection, value.ToReadOnly(), value);

            // and return it
            return (ReadOnlyCollection<T>)collection;
        }

        public static void ValidateArgumentTypes(MethodBase method, ExpressionType nodeKind, ref ReadOnlyCollection<Expression> arguments, string? methodParamName)
        {
            Debug.Assert(nodeKind == ExpressionType.Invoke || nodeKind == ExpressionType.Call || nodeKind == ExpressionType.Dynamic || nodeKind == ExpressionType.New);

            ParameterInfo[] pis = GetParametersForValidation(method, nodeKind);

            ValidateArgumentCount(method, nodeKind, arguments.Count, pis);

            Expression[]? newArgs = null;
            for (int i = 0, n = pis.Length; i < n; i++)
            {
                Expression arg = arguments[i];
                ParameterInfo pi = pis[i];
                arg = ValidateOneArgument(method, nodeKind, arg, pi, methodParamName, nameof(arguments), i);

                if (newArgs == null && arg != arguments[i])
                {
                    newArgs = new Expression[arguments.Count];
                    for (int j = 0; j < i; j++)
                    {
                        newArgs[j] = arguments[j];
                    }
                }
                newArgs?[i] = arg;
            }
            if (newArgs != null)
            {
                arguments = new TrueReadOnlyCollection<Expression>(newArgs);
            }
        }

        public static void ValidateArgumentCount(MethodBase method, ExpressionType nodeKind, int count, ParameterInfo[] pis)
        {
            if (pis.Length != count)
            {
                // Throw the right error for the node we were given
                switch (nodeKind)
                {
                    case ExpressionType.New:
                        throw Error.IncorrectNumberOfConstructorArguments();
                    case ExpressionType.Invoke:
                        throw Error.IncorrectNumberOfLambdaArguments();
                    case ExpressionType.Dynamic:
                    case ExpressionType.Call:
                        throw Error.IncorrectNumberOfMethodCallArguments(method, nameof(method));
                    default:
                        throw ContractUtils.Unreachable;
                }
            }
        }

        public static Expression ValidateOneArgument(MethodBase method, ExpressionType nodeKind, Expression arguments, ParameterInfo pi, string? methodParamName, string argumentParamName, int index = -1)
        {
            RequiresCanRead(arguments, argumentParamName, index);
            Type pType = pi.ParameterType;
            if (pType.IsByRef)
            {
                pType = pType.GetElementType()!;
            }

            TypeUtils.ValidateType(pType, methodParamName, allowByRef: true, allowPointer: true);
            if (!TypeUtils.AreReferenceAssignable(pType, arguments.Type))
            {
                if (!TryQuote(pType, ref arguments))
                {
                    // Throw the right error for the node we were given
                    switch (nodeKind)
                    {
                        case ExpressionType.New:
                            throw Error.ExpressionTypeDoesNotMatchConstructorParameter(arguments.Type, pType, argumentParamName, index);
                        case ExpressionType.Invoke:
                            throw Error.ExpressionTypeDoesNotMatchParameter(arguments.Type, pType, argumentParamName, index);
                        case ExpressionType.Dynamic:
                        case ExpressionType.Call:
                            throw Error.ExpressionTypeDoesNotMatchMethodParameter(arguments.Type, pType, method, argumentParamName, index);
                        default:
                            throw ContractUtils.Unreachable;
                    }
                }
            }
            return arguments;
        }

        public static void RequiresCanRead(Expression expression, string paramName)
        {
            RequiresCanRead(expression, paramName, -1);
        }

        public static void RequiresCanRead(Expression expression, string paramName, int idx)
        {
            ContractUtils.RequiresNotNull(expression, paramName, idx);

            // validate that we can read the node
            switch (expression.NodeType)
            {
                case ExpressionType.Index:
                    IndexExpression index = (IndexExpression)expression;
                    if (index.Indexer != null && !index.Indexer.CanRead)
                    {
                        throw Error.ExpressionMustBeReadable(paramName, idx);
                    }
                    break;
                case ExpressionType.MemberAccess:
                    MemberExpression member = (MemberExpression)expression;
                    if (member.Member is PropertyInfo prop)
                    {
                        if (!prop.CanRead)
                        {
                            throw Error.ExpressionMustBeReadable(paramName, idx);
                        }
                    }
                    break;
            }
        }

        // Attempts to auto-quote the expression tree. Returns true if it succeeded, false otherwise.
        public static bool TryQuote(Type parameterType, ref Expression argument)
        {
            // We used to allow quoting of any expression, but the behavior of
            // quote (produce a new tree closed over parameter values), only
            // works consistently for lambdas
            return ExpressionValidation.TryQuote(parameterType, ref argument);
        }

        internal static ParameterInfo[] GetParametersForValidation(MethodBase method, ExpressionType nodeKind)
        {
            ParameterInfo[] pis = method.GetParameters();

            if (nodeKind == ExpressionType.Dynamic)
            {
                pis = pis.RemoveFirst(); // ignore CallSite argument
            }
            return pis;
        }

        internal static bool SameElements<T>(ICollection<T>? replacement, IReadOnlyList<T> current) where T : class
        {
            Debug.Assert(current != null);
            if (replacement == current) // Relatively common case, so particularly useful to take the short-circuit.
            {
                return true;
            }

            if (replacement == null) // Treat null as empty.
            {
                return current.Count == 0;
            }

            return SameElementsInCollection(replacement, current);
        }

        internal static bool SameElements<T>(ref IEnumerable<T>? replacement, IReadOnlyList<T> current) where T : class
        {
            Debug.Assert(current != null);
            if (replacement == current) // Relatively common case, so particularly useful to take the short-circuit.
            {
                return true;
            }

            if (replacement == null) // Treat null as empty.
            {
                return current.Count == 0;
            }

            // Ensure arguments is safe to enumerate twice.
            // If we have to build a collection, build a TrueReadOnlyCollection<T>
            // so it won't be built a second time if used.
            ICollection<T>? replacementCol = replacement as ICollection<T>;
            if (replacementCol == null)
            {
                replacement = replacementCol = replacement.ToReadOnly();
            }

            return SameElementsInCollection(replacementCol, current);
        }

        private static bool SameElementsInCollection<T>(ICollection<T> replacement, IReadOnlyList<T> current) where T : class
        {
            int count = current.Count;
            if (replacement.Count != count)
            {
                return false;
            }

            if (count != 0)
            {
                int index = 0;
                foreach (T replacementObject in replacement)
                {
                    if (replacementObject != current[index])
                    {
                        return false;
                    }

                    index++;
                }
            }

            return true;
        }
    }
}
