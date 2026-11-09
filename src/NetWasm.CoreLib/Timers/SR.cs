// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Generated from dotnet/runtime 474ed963699fc29809015392738a15231aaae2de resources;
// only the strings this namespace references are included.

namespace System.Timers
{
    internal static class SR
    {
        internal const string InvalidParameter =
            "Invalid value '{1}' for parameter '{0}'.";
        internal const string TimerAutoReset =
            "Indicates whether the timer will be restarted when it is enabled.";
        internal const string TimerEnabled =
            "Indicates whether the timer is enabled to fire events at a defined interval.";
        internal const string TimerInterval =
            "The number of milliseconds between timer events.";
        internal const string TimerIntervalElapsed =
            "Occurs when the Interval has elapsed.";
        internal const string TimerInvalidInterval =
            "'{0}' is not a valid value for 'Interval'. 'Interval' must be greater than {1}.";
        internal const string TimerSynchronizingObject =
            "The object used to marshal the event handler calls issued when an interval has elapsed.";

        internal static string Format(string format, object? arg0) =>
            string.Format(format, arg0);

        internal static string Format(string format, object? arg0, object? arg1) =>
            string.Format(format, arg0, arg1);

        internal static string Format(string format, object? arg0, object? arg1, object? arg2) =>
            string.Format(format, arg0, arg1, arg2);
    }
}
