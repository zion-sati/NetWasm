// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Adapted from dotnet/runtime deae2e32bf0e018e09d64597b7c59b62b73bb3ce.
// Explicit code/message storage and formatting retain upstream behavior.
// The constructors are upstream's: the last P/Invoke error is native AOT's, and an error's message is
// the WASI C library's (Interop.Sys.StrError), as upstream's Unix GetPInvokeErrorMessage asks strerror.
// Legacy formatter serialization is under the reflection limitation.
using System.Globalization;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;
using System.Text;

namespace System.ComponentModel
{
    [Serializable]
    [System.Runtime.CompilerServices.TypeForwardedFrom("System, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089")]
    public class Win32Exception : ExternalException
    {
        private const int E_FAIL = unchecked((int)0x80004005);

        public Win32Exception() : this(Marshal.GetLastPInvokeError()) { }
        public Win32Exception(int error) : this(error, Marshal.GetPInvokeErrorMessage(error)) { }
        public Win32Exception(int error, string? message) : base(message) { NativeErrorCode = error; }
        public Win32Exception(string? message) : this(Marshal.GetLastPInvokeError(), message) { }
        public Win32Exception(string? message, Exception? innerException) : base(message, innerException) { NativeErrorCode = Marshal.GetLastPInvokeError(); }
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
