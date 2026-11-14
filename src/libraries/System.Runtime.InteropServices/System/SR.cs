// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// The strings System.Runtime.InteropServices uses, from CoreLib's SR classes (dotnet/runtime deae2e32bf0e018e09d64597b7c59b62b73bb3ce)
// or System.Runtime.InteropServices's Strings.resx at that commit.

namespace System
{
    internal static partial class SR
    {
        internal const string AmbiguousMatch_MultipleEventInterfaceAttributes = "More than one ComEventInterfaceAttribute found for '{0}' on the declaring interface of the event.";

        internal static string Format(string resourceFormat, object? p1) => string.Format(resourceFormat, p1);

        internal static string Format(string resourceFormat, object? p1, object? p2) => string.Format(resourceFormat, p1, p2);

        internal static string Format(string resourceFormat, object? p1, object? p2, object? p3) => string.Format(resourceFormat, p1, p2, p3);

        internal static string Format(string resourceFormat, params object?[]? args) =>
            args is null ? resourceFormat : string.Format(resourceFormat, args);

        internal static string Format(IFormatProvider? provider, string resourceFormat, params object?[]? args) =>
            args is null ? resourceFormat : string.Format(provider, resourceFormat, args);
        internal const string InvalidOperation_NoComEventInterfaceAttribute = "Event invocation for COM objects requires the declaring interface of the event to be attributed with ComEventInterfaceAttribute.";
        internal const string InvalidOperation_NoDispIdAttribute = "Event invocation for COM objects requires event to be attributed with DispIdAttribute.";
        internal const string PlatformNotSupported_EventReflection = "COM-aware event lookup reads the event through reflection, which NetWasm does not support.";
        internal const string Arg_InvalidThreshold = "maximumThreshold cannot be less than initialThreshold.";
        internal const string Arg_NeedNonNegNumRequired = "Non-negative number required.";
        internal const string InvalidOperation_HCCountOverflow = "Handle collector count overflows or underflows.";
    }
}
