// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// The strings System.Net.Primitives uses, from CoreLib's SR classes (dotnet/runtime deae2e32bf0e018e09d64597b7c59b62b73bb3ce)
// or System.Net.Primitives's Strings.resx at that commit.

namespace System
{
    internal static partial class SR
    {

        internal static string Format(string resourceFormat, object? p1) => string.Format(resourceFormat, p1);

        internal static string Format(string resourceFormat, object? p1, object? p2) => string.Format(resourceFormat, p1, p2);

        internal static string Format(string resourceFormat, object? p1, object? p2, object? p3) => string.Format(resourceFormat, p1, p2, p3);

        internal static string Format(string resourceFormat, params object?[]? args) =>
            args is null ? resourceFormat : string.Format(resourceFormat, args);

        internal static string Format(IFormatProvider? provider, string resourceFormat, params object?[]? args) =>
            args is null ? resourceFormat : string.Format(provider, resourceFormat, args);
        internal const string InvalidOperation_EnumFailedVersion = "Collection was modified after the enumerator was instantiated.";
        internal const string InvalidOperation_EnumOpCantHappen = "Enumeration has either not started or has already finished.";
        internal const string net_nodefaultcreds = "Default credentials cannot be supplied for the {0} authentication scheme.";
        internal const string net_collection_readonly = "The collection is read-only.";
    }
}
