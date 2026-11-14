// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// NetWasm counterpart of upstream DebugProvider.Unix.cs (runtime 474ed96). NetWasm has no
// attached managed debugger: debug output goes to stderr, and a failed assertion throws
// InvalidOperationException deterministically instead of terminating the process.

namespace System.Diagnostics
{
    public partial class DebugProvider
    {
        public static void FailCore(string stackTrace, string? message, string? detailMessage, string errorSource)
        {
            if (s_FailCore != null)
            {
                s_FailCore(stackTrace, message, detailMessage, errorSource);
                return;
            }

            throw new InvalidOperationException(CreateFailureMessage(message, detailMessage));
        }

        public static void WriteCore(string message)
        {
            if (s_WriteCore != null)
            {
                s_WriteCore(message);
                return;
            }

            Console.Error.Write(message);
        }

        private static string? CreateFailureMessage(string? message, string? detailMessage)
        {
            if (string.IsNullOrEmpty(message))
            {
                return detailMessage;
            }

            return string.IsNullOrEmpty(detailMessage)
                ? message
                : message + " " + detailMessage;
        }
    }
}
