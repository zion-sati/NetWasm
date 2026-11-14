// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Ported from dotnet/runtime commit deae2e32bf0e018e09d64597b7c59b62b73bb3ce.
// NetWasm adaptation: executing a query rewrites it to Enumerable calls by reflection and compiles it,
// and the non-generic factories construct generic types at run time, so Execute, enumeration of an
// expression-built query and those factories throw PlatformNotSupportedException after upstream's
// argument checks. Wrapping an enumerable, CreateQuery<TElement> and ToString are upstream's.
// System.Linq.Expressions' SR and Error supply the strings and argument factory: CoreLib keeps no
// internal types in System.Linq, where its friend assemblies declare their own SR (CS0436).

using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;

namespace System.Linq
{
    public abstract class EnumerableQuery
    {
        internal abstract Expression Expression { get; }
        internal abstract IEnumerable? Enumerable { get; }

        internal EnumerableQuery() { }

        [RequiresUnreferencedCode(Queryable.InMemoryQueryableExtensionMethodsRequiresUnreferencedCode)]
        [RequiresDynamicCode(Queryable.InMemoryQueryableExtensionMethodsRequiresDynamicCode)]
        internal static IQueryable Create(Type elementType, IEnumerable sequence)
        {
            throw new PlatformNotSupportedException(SR.PlatformNotSupported_QueryableConstruction);
        }

        [RequiresUnreferencedCode(Queryable.InMemoryQueryableExtensionMethodsRequiresUnreferencedCode)]
        [RequiresDynamicCode(Queryable.InMemoryQueryableExtensionMethodsRequiresDynamicCode)]
        internal static IQueryable Create(Type elementType, Expression expression)
        {
            throw new PlatformNotSupportedException(SR.PlatformNotSupported_QueryableConstruction);
        }
    }

    [RequiresDynamicCode(Queryable.InMemoryQueryableExtensionMethodsRequiresDynamicCode)]
    [RequiresUnreferencedCode(Queryable.InMemoryQueryableExtensionMethodsRequiresUnreferencedCode)]
    public class EnumerableQuery<T> : EnumerableQuery, IOrderedQueryable<T>, IQueryProvider
    {
        private readonly Expression _expression;
        private IEnumerable<T>? _enumerable;

        IQueryProvider IQueryable.Provider => this;

        public EnumerableQuery(IEnumerable<T> enumerable)
        {
            _enumerable = enumerable;
            _expression = Expression.Constant(this);
        }

        public EnumerableQuery(Expression expression)
        {
            _expression = expression;
        }

        internal override Expression Expression => _expression;

        internal override IEnumerable? Enumerable => _enumerable;

        Expression IQueryable.Expression => _expression;

        Type IQueryable.ElementType => typeof(T);

        IQueryable IQueryProvider.CreateQuery(Expression expression)
        {
            ArgumentNullException.ThrowIfNull(expression);

            throw new PlatformNotSupportedException(SR.PlatformNotSupported_QueryableConstruction);
        }

        IQueryable<TElement> IQueryProvider.CreateQuery<TElement>(Expression expression)
        {
            ArgumentNullException.ThrowIfNull(expression);

            if (!typeof(IQueryable<TElement>).IsAssignableFrom(expression.Type))
            {
                throw Error.ArgumentNotValid(nameof(expression));
            }
            return new EnumerableQuery<TElement>(expression);
        }

        object? IQueryProvider.Execute(Expression expression)
        {
            ArgumentNullException.ThrowIfNull(expression);

            throw new PlatformNotSupportedException(SR.PlatformNotSupported_QueryExecution);
        }

        TElement IQueryProvider.Execute<TElement>(Expression expression)
        {
            ArgumentNullException.ThrowIfNull(expression);

            if (!typeof(TElement).IsAssignableFrom(expression.Type))
                throw Error.ArgumentNotValid(nameof(expression));
            throw new PlatformNotSupportedException(SR.PlatformNotSupported_QueryExecution);
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        IEnumerator<T> IEnumerable<T>.GetEnumerator() => GetEnumerator();

        private IEnumerator<T> GetEnumerator()
        {
            if (_enumerable == null)
            {
                throw new PlatformNotSupportedException(SR.PlatformNotSupported_QueryExecution);
            }
            return _enumerable.GetEnumerator();
        }

#pragma warning disable CS8764 // NetWasm declares Object.ToString as non-nullable string
        public override string? ToString()
        {
            if (_expression is ConstantExpression c && c.Value == this)
            {
                if (_enumerable != null)
                    return _enumerable.ToString();
                return "null";
            }
            return _expression.ToString();
        }
#pragma warning restore CS8764
    }
}
