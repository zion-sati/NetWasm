// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Adapted from dotnet/runtime 474ed963699fc29809015392738a15231aaae2de.
// Explicit code/message storage and formatting retain upstream behavior.
// Host last-error/message lookup and legacy serialization are deferred. Derived managed exceptions
// that no native operation produced (WebSocketException) pass an explicit native code, with or
// without an inner exception, instead of capturing the last error.
using System.Globalization;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;
using System.Text;

namespace System.ComponentModel
{
    [Serializable]
    public class Win32Exception : ExternalException
    {
        private const int E_FAIL = unchecked((int)0x80004005);

        public Win32Exception() => throw new PlatformNotSupportedException("Win32Exception host last-error lookup is not available on this NetWasm platform.");
        public Win32Exception(int error) => throw new PlatformNotSupportedException("Win32Exception host error-message lookup is not available on this NetWasm platform.");
        public Win32Exception(int error, string? message) : base(message) { NativeErrorCode = error; }
        internal Win32Exception(int error, string? message, Exception? innerException) : base(message, innerException) { NativeErrorCode = error; }
        public Win32Exception(string? message) => throw new PlatformNotSupportedException("Win32Exception host last-error lookup is not available on this NetWasm platform.");
        public Win32Exception(string? message, Exception? innerException) => throw new PlatformNotSupportedException("Win32Exception host last-error lookup is not available on this NetWasm platform.");
        protected Win32Exception(SerializationInfo info, StreamingContext context) => throw new PlatformNotSupportedException("Win32Exception formatter deserialization is not available on this NetWasm platform.");
        [Obsolete(Obsoletions.LegacyFormatterImplMessage, DiagnosticId = Obsoletions.LegacyFormatterImplDiagId, UrlFormat = Obsoletions.SharedUrlFormat)]
        public override void GetObjectData(SerializationInfo info, StreamingContext context) => throw new PlatformNotSupportedException("Win32Exception formatter serialization is not available on this NetWasm platform.");
        public int NativeErrorCode { get; }

        public override string ToString()
        {
            if (NativeErrorCode == 0 || NativeErrorCode == HResult)
            {
                return base.ToString();
            }

            string message = Message;
            string className = GetType().ToString();
            StringBuilder s = new StringBuilder(className);
            string nativeErrorString = NativeErrorCode < 0
                ? $"0x{NativeErrorCode:X8}"
                : NativeErrorCode.ToString(CultureInfo.InvariantCulture);
            if (HResult == E_FAIL)
            {
                s.Append($" ({nativeErrorString})");
            }
            else
            {
                s.Append($" ({HResult:X8}, {nativeErrorString})");
            }

            if (!(string.IsNullOrEmpty(message)))
            {
                s.Append(": ");
                s.Append(message);
            }

            Exception? innerException = InnerException;
            if (innerException != null)
            {
                s.Append(" ---> ");
                s.Append(innerException.ToString());
            }

            string? stackTrace = StackTrace;
            if (stackTrace != null)
            {
                s.AppendLine();
                s.Append(stackTrace);
            }

            return s.ToString();
        }
    }
}
