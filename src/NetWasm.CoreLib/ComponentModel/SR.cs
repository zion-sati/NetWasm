// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Generated from dotnet/runtime deae2e32bf0e018e09d64597b7c59b62b73bb3ce resources;
// only the strings this namespace references are included.

namespace System.ComponentModel
{
    internal static class SR
    {
        internal const string InvalidArgumentValue =
            "Value of '{0}' cannot be empty.";
        internal const string InvalidNullArgument =
            "Null is not a valid value for {0}.";
        internal const string BinaryFormatterMessage =
            "BinaryFormatter serialization is obsolete and should not be used. See https://aka.ms/binaryformatter for more information.";

        // Upstream SR answers from the System.Resources.UseSystemResourceKeys switch.
        internal static bool UsingResourceKeys() =>
            AppContext.TryGetSwitch("System.Resources.UseSystemResourceKeys", out bool usingResourceKeys) && usingResourceKeys;

        // NetWasm strings for its platform boundaries.

        internal static string Format(string format, object? arg0) =>
            string.Format(format, arg0);

        internal static string Format(string format, object? arg0, object? arg1) =>
            string.Format(format, arg0, arg1);

        internal static string Format(string format, object? arg0, object? arg1, object? arg2) =>
            string.Format(format, arg0, arg1, arg2);

        internal static string Format(string format, params object?[] args) =>
            string.Format(format, args);
    }
}
