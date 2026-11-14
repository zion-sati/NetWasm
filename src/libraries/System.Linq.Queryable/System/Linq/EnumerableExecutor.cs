// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Ported from dotnet/runtime commit deae2e32bf0e018e09d64597b7c59b62b73bb3ce.
// NetWasm adaptation: execution rewrites the query by reflection and compiles it, so Execute and
// the non-generic factory throw PlatformNotSupportedException.
// System.Linq.Expressions' SR supplies the strings: CoreLib keeps no internal types in System.Linq,
// where its friend assemblies declare their own SR (CS0436).

using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;

namespace System.Linq
{
    public abstract class EnumerableExecutor
    {
        [RequiresUnreferencedCode(Queryable.InMemoryQueryableExtensionMethodsRequiresUnreferencedCode)]
        [RequiresDynamicCode(Queryable.InMemoryQueryableExtensionMethodsRequiresDynamicCode)]
        internal abstract object? ExecuteBoxed();

        internal EnumerableExecutor() { }

        [RequiresDynamicCode(Queryable.InMemoryQueryableExtensionMethodsRequiresDynamicCode)]
        internal static EnumerableExecutor Create(Expression expression)
        {
            throw new PlatformNotSupportedException(SR.PlatformNotSupported_QueryExecution);
        }
    }

    public class EnumerableExecutor<T> : EnumerableExecutor
    {
        private readonly Expression _expression;

        public EnumerableExecutor(Expression expression)
        {
            _expression = expression;
        }

        [RequiresUnreferencedCode(Queryable.InMemoryQueryableExtensionMethodsRequiresUnreferencedCode)]
        [RequiresDynamicCode(Queryable.InMemoryQueryableExtensionMethodsRequiresDynamicCode)]
        internal override object? ExecuteBoxed() => Execute();

        [RequiresUnreferencedCode(Queryable.InMemoryQueryableExtensionMethodsRequiresUnreferencedCode)]
        [RequiresDynamicCode(Queryable.InMemoryQueryableExtensionMethodsRequiresDynamicCode)]
        internal T Execute()
        {
            throw new PlatformNotSupportedException(SR.PlatformNotSupported_QueryExecution);
        }
    }
}
