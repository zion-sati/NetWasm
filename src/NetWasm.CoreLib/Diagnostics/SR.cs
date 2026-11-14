// NetWasm adaptation of the pinned DiagnosticSource resource identifiers.
// Values remain stable for the retained public behavior; excluded resource paths are omitted.

using System;

namespace System.Diagnostics
{
    internal static class SR
    {
        internal const string DebugAssertBanner =
            "---- DEBUG ASSERTION FAILED ----";
        internal const string DebugAssertLongMessage =
            "---- Assert Long Message ----";
        internal const string DebugAssertShortMessage =
            "---- Assert Short Message ----";
        internal const string ActivityIdFormatInvalid = "\"Value must be a valid ActivityIdFormat value\"";
        internal const string ActivityListener_RefreshSourceFilters_PredicateThrew = "One or more 'ShouldListenTo' callbacks threw while refreshing the listener's source filters. See InnerExceptions for details.";
        internal const string ActivityNotRunning = "Trying to set an Activity that is not running";
        internal const string ActivityNotStarted = "\"Can not stop an Activity that was not started\"";
        internal const string ActivityAddExceptionNotSupported = "Activity.AddException is not supported by the portable diagnostics profile.";
        internal const string ActivitySetParentAlreadyStarted = "\"Can not set parent on already started Activity\"";
        internal const string ActivityStartAlreadyStarted = "\"Can not start an Activity that was already started\"";
        internal const string Arg_BufferTooSmall = "Destination buffer is not long enough to copy all the items in the list.";
        internal const string EndTimeNotUtc = "\"EndTime is not UTC\"";
        internal const string InvalidActivitySourceScope = "The activity source factory does not allow a custom scope value when creating an activity source.";
        internal const string InvalidTraceParent = "\"Invalid trace parent.\"";
        internal const string KeyAlreadyExist = "\"The collection already contains item with same key '{0}''\"";
        internal const string OperationNameInvalid = "\"OperationName must not be null or empty\"";
        internal const string ParentIdAlreadySet = "\"ParentId is already set\"";
        internal const string ParentIdInvalid = "\"ParentId must not be null or empty\"";
        internal const string SetFormatOnStartedActivity = "\"Can not change format for an activity that was already started\"";
        internal const string SetParentIdOnActivityWithParent = "\"Can not set ParentId on activity which has parent\"";
        internal const string StartTimeNotUtc = "\"StartTime is not UTC\"";
        internal const string InvalidHistogramExplicitBucketBoundaries = "Histogram explicit bucket boundaries must be specified in ascending order and cannot contain duplicate values.";
        internal const string UnsupportedType = "The metric value type is not supported by the portable diagnostics profile.";

        // System.Diagnostics.Process resources (dotnet/runtime 474ed96 Strings.resx) and the NetWasm
        // platform-boundary messages for processes, process handles, stack traces and file versions.
        internal const string StackTraceNotSupported = "Stack trace capture requires NetWasm's managed stack trace instrumentation (NetWasmManagedStackTrace).";

        internal static string Format(string format, object? arg) =>
            format.Replace("{0}", arg?.ToString() ?? string.Empty);

        internal static string Format(string format, object? arg0, object? arg1) =>
            format.Replace("{0}", arg0?.ToString() ?? string.Empty)
                .Replace("{1}", arg1?.ToString() ?? string.Empty);
    }
}
