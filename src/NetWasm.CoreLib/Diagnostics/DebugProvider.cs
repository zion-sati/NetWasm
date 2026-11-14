// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Ported from dotnet/runtime commit deae2e32bf0e018e09d64597b7c59b62b73bb3ce.
// NetWasm adaptation: no stack trace is captured for assertion failures (stack discovery is
// outside the bounded stack contracts), and the default provider writes no assertion banner:
// without a debugger upstream sends it to syslog, which NetWasm lacks; the exception carries
// the message. Environment.NewLine replaces the internal NewLineConst (same value).

// Do not remove this, it is needed to retain calls to these conditional methods in release builds
#define DEBUG

using System.Diagnostics.CodeAnalysis;

namespace System.Diagnostics
{
    /// <summary>
    /// Provides default implementation for Write and Fail methods in Debug class.
    /// </summary>
    public partial class DebugProvider
    {
        [DoesNotReturn]
        public virtual void Fail(string? message, string? detailMessage)
        {
            string stackTrace = "";
            FailCore(stackTrace, message, detailMessage, "Assertion failed.");
#pragma warning disable 8763 // "A method marked [DoesNotReturn] should not return."
        }
#pragma warning restore 8763

        internal void WriteAssert(string stackTrace, string? message, string? detailMessage)
        {
            WriteLine(SR.DebugAssertBanner + Environment.NewLine
                   + SR.DebugAssertShortMessage + Environment.NewLine
                   + message + Environment.NewLine
                   + SR.DebugAssertLongMessage + Environment.NewLine
                   + detailMessage + Environment.NewLine
                   + stackTrace);
        }

        public virtual void Write(string? message)
        {
            lock (s_lock)
            {
                if (message == null)
                {
                    WriteCore(string.Empty);
                    return;
                }
                if (_needIndent)
                {
                    message = GetIndentString() + message;
                    _needIndent = false;
                }
                WriteCore(message);
                if (message.EndsWith(Environment.NewLine, StringComparison.Ordinal))
                {
                    _needIndent = true;
                }
            }
        }

        public virtual void WriteLine(string? message)
        {
            Write(message + Environment.NewLine);
        }

        public virtual void OnIndentLevelChanged(int indentLevel) { }

        public virtual void OnIndentSizeChanged(int indentSize) { }

        private static readonly object s_lock = new object();

        private sealed class DebugAssertException : Exception
        {
            internal DebugAssertException(string? message, string? detailMessage, string? stackTrace) :
                base(Terminate(message) + Terminate(detailMessage) + stackTrace)
            {
            }

            private static string? Terminate(string? s)
            {
                if (s == null)
                    return s;

                s = s.Trim();
                if (s.Length > 0)
                    s += Environment.NewLine;

                return s;
            }
        }

        private bool _needIndent = true;

        private string? _indentString;

        private string GetIndentString()
        {
            int indentCount = Debug.IndentSize * Debug.IndentLevel;
            if (_indentString?.Length == indentCount)
            {
                return _indentString;
            }
            return _indentString = new string(' ', indentCount);
        }

        // internal and not readonly so that the tests can swap this out.
        internal static Action<string, string?, string?, string>? s_FailCore;
        internal static Action<string>? s_WriteCore;
    }
}
