// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// The strings System.Diagnostics.TraceSource uses, from CoreLib's SR classes (dotnet/runtime deae2e32bf0e018e09d64597b7c59b62b73bb3ce)
// or System.Diagnostics.TraceSource's Strings.resx at that commit.

namespace System
{
    internal static partial class SR
    {
        internal const string DebugAssertBanner = "---- DEBUG ASSERTION FAILED ----";
        internal const string DebugAssertLongMessage = "---- Assert Long Message ----";
        internal const string DebugAssertShortMessage = "---- Assert Short Message ----";
        internal const string ExceptionOccurred = "An exception occurred while writing to the log file {0}: {1}.";

        internal static string Format(string resourceFormat, object? p1) => string.Format(resourceFormat, p1);

        internal static string Format(string resourceFormat, object? p1, object? p2) => string.Format(resourceFormat, p1, p2);

        internal static string Format(string resourceFormat, object? p1, object? p2, object? p3) => string.Format(resourceFormat, p1, p2, p3);

        internal static string Format(string resourceFormat, params object?[]? args) =>
            args is null ? resourceFormat : string.Format(resourceFormat, args);

        internal static string Format(IFormatProvider? provider, string resourceFormat, params object?[]? args) =>
            args is null ? resourceFormat : string.Format(provider, resourceFormat, args);
        internal const string TraceListenerFail = "Fail:";
        internal const string TraceListenerIndentSize = "The IndentSize property must be non-negative.";
        internal const string MustAddListener = "Only TraceListeners can be added to a TraceListenerCollection.";
        internal const string TraceSwitchInvalidLevel = "The Level must be set to a value in the enumeration TraceLevel.";
        internal const string TraceSwitchLevelTooHigh = "Attempted to set {0} to a value that is too high.  Setting level to TraceLevel.Verbose";
        internal const string TraceSwitchLevelTooLow = "Attempted to set {0} to a value that is too low.  Setting level to TraceLevel.Off";
        internal const string AttributeNotSupported = "'{0}' is not a valid attribute for type '{1}'.";
    }
}
