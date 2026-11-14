// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// The strings System.Net.NetworkInformation uses, from CoreLib's SR classes (dotnet/runtime deae2e32bf0e018e09d64597b7c59b62b73bb3ce)
// or System.Net.NetworkInformation's Strings.resx at that commit.

namespace System
{
    internal static partial class SR
    {
        internal const string net_collection_readonly = "The collection is read-only.";
        internal const string net_MethodNotImplementedException = "This method is not implemented by this class.";
        internal const string net_PropertyNotImplementedException = "This property is not implemented by this class.";

        internal static string Format(string resourceFormat, object? p1) => string.Format(resourceFormat, p1);

        internal static string Format(string resourceFormat, object? p1, object? p2) => string.Format(resourceFormat, p1, p2);

        internal static string Format(string resourceFormat, object? p1, object? p2, object? p3) => string.Format(resourceFormat, p1, p2, p3);

        internal static string Format(string resourceFormat, params object?[]? args) =>
            args is null ? resourceFormat : string.Format(resourceFormat, args);

        internal static string Format(IFormatProvider? provider, string resourceFormat, params object?[]? args) =>
            args is null ? resourceFormat : string.Format(provider, resourceFormat, args);
        internal const string net_bad_mac_address = "An invalid physical address was specified: '{0}'.";
    }
}
