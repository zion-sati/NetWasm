// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// The strings System.Linq.Queryable uses, from CoreLib's SR classes (dotnet/runtime deae2e32bf0e018e09d64597b7c59b62b73bb3ce)
// or System.Linq.Queryable's Strings.resx at that commit.

namespace System
{
    internal static partial class SR
    {
        internal const string ArgumentNotIEnumerableGeneric = "{0} is not IEnumerable<>";
        internal const string ArgumentNotValid = "Argument {0} is not valid";
        internal const string EnumeratingNullEnumerableExpression = "Cannot enumerate a query created from a null IEnumerable<>";

        internal static string Format(string resourceFormat, object? p1) => string.Format(resourceFormat, p1);

        internal static string Format(string resourceFormat, object? p1, object? p2) => string.Format(resourceFormat, p1, p2);

        internal static string Format(string resourceFormat, object? p1, object? p2, object? p3) => string.Format(resourceFormat, p1, p2, p3);

        internal static string Format(string resourceFormat, params object?[]? args) =>
            args is null ? resourceFormat : string.Format(resourceFormat, args);

        internal static string Format(IFormatProvider? provider, string resourceFormat, params object?[]? args) =>
            args is null ? resourceFormat : string.Format(provider, resourceFormat, args);
        internal const string NoMethodOnType = "There is no method '{0}' on type '{1}'";
        internal const string NoMethodOnTypeMatchingArguments = "There is no method '{0}' on type '{1}' that matches the specified arguments";
        internal const string PlatformNotSupported_QueryExecution = "NetWasm cannot execute an in-memory query: rewriting it to Enumerable calls needs reflection and run-time code generation.";
        internal const string PlatformNotSupported_QueryableConstruction = "NetWasm cannot construct a generic query type at run time; use AsQueryable<TElement> or IQueryProvider.CreateQuery<TElement>.";
    }
}
