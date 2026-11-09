// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Generated from dotnet/runtime 474ed963699fc29809015392738a15231aaae2de resources;
// only the strings this namespace references are included.

namespace System.IO.IsolatedStorage
{
    internal static class SR
    {
        internal const string IsolatedStorage_ApplicationUndefined =
            "Application cannot be determined on an Assembly or Domain store.";
        internal const string IsolatedStorage_AssemblyUndefined =
            "Assembly cannot be determined for an Application store.";
        internal const string IsolatedStorage_CurrentSizeUndefined =
            "Current size cannot be determined for this store.";
        internal const string IsolatedStorage_Exception =
            "An error occurred while accessing IsolatedStorage.";
        internal const string IsolatedStorage_PlatformNotSupported =
            "System.IO.IsolatedStorage is not supported on this platform.";
        internal const string IsolatedStorage_QuotaIsUndefined =
            "{0} is not defined for this store. An operation was performed that requires access to {0}. Stores obtained using enumeration APIs do not have a well-defined {0}, since partial evidence is used to open the store.";
        internal const string IsolatedStorage_Scope_Invalid =
            "Invalid scope.";

        internal static string Format(string format, object? arg0) =>
            string.Format(format, arg0);

        internal static string Format(string format, object? arg0, object? arg1) =>
            string.Format(format, arg0, arg1);

        internal static string Format(string format, object? arg0, object? arg1, object? arg2) =>
            string.Format(format, arg0, arg1, arg2);
    }
}
