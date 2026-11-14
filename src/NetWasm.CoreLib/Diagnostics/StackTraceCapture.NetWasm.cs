// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// NetWasm stack capture for StackTrace and StackFrame, in place of the runtime stack walk that
// dotnet/runtime commit deae2e32bf0e018e09d64597b7c59b62b73bb3ce uses (StackFrameHelper on CoreCLR,
// RhGetCurrentThreadStackTrace on native AOT). With NetWasm's opt-in managed stack trace
// (NetWasmManagedStackTrace, or the compiler's --stack-trace-symbols), the runtime formats the
// managed frames when an exception is thrown, innermost first and one per line, as
// "at <method>" or "at <method> in <document>:line <n>", and stores them as the exception's stack
// trace. Frames are read back from that text; the current thread's frames come from throwing and
// catching a private exception. Without the instrumentation no trace is captured, and capture
// reports that frames are unavailable.

using System.Runtime.CompilerServices;

namespace System.Diagnostics
{
    internal static class StackTraceCapture
    {
        private const string FramePrefix = "at ";
        private const string LocationSeparator = " in ";
        private const string LineSeparator = ":line ";
        private const string CoreLibQualifier = "[NetWasm.CoreLib]";

        /// <summary>
        /// Returns the current thread's frames, starting at the caller of the StackTrace or
        /// StackFrame constructor, or null when stack traces are not instrumented.
        /// </summary>
        internal static StackFrame[]? CaptureCurrentThread(bool needFileInfo)
        {
            string? trace = CaptureCurrentThreadTrace();
            if (trace is null)
            {
                return null;
            }

            string[] lines = trace.Split('\n');
            int first = 0;
            while (first < lines.Length && IsCaptureFrame(lines[first]))
            {
                first++;
            }
            return CreateFrames(lines, first, needFileInfo);
        }

        /// <summary>
        /// Returns the frames captured when the exception was thrown, none when it has not been
        /// thrown, or null when stack traces are not instrumented.
        /// </summary>
        internal static StackFrame[]? CaptureException(Exception exception, bool needFileInfo)
        {
            string? trace = exception.CapturedStackTrace;
            if (trace is null)
            {
                return CaptureCurrentThreadTrace() is null ? null : [];
            }
            return CreateFrames(trace.Split('\n'), 0, needFileInfo);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static string? CaptureCurrentThreadTrace()
        {
            try
            {
                ThrowForCapture();
            }
            catch (StackTraceCaptureException exception)
            {
                return exception.CapturedStackTrace;
            }
            return null;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void ThrowForCapture() => throw new StackTraceCaptureException();

        // The capture probe and the StackTrace and StackFrame members that call it sit above the
        // caller's frame; desktop .NET leaves its own frames out of the trace in the same way.
        private static bool IsCaptureFrame(string line)
        {
            ReadOnlySpan<char> method = MethodText(line);
            if (method.StartsWith(CoreLibQualifier, StringComparison.Ordinal))
            {
                method = method.Slice(CoreLibQualifier.Length);
            }
            return method.StartsWith("System.Diagnostics.StackTrace", StringComparison.Ordinal) ||
                method.StartsWith("System.Diagnostics.StackFrame", StringComparison.Ordinal);
        }

        private static ReadOnlySpan<char> MethodText(string line)
        {
            ReadOnlySpan<char> text = line.AsSpan();
            if (text.StartsWith(FramePrefix, StringComparison.Ordinal))
            {
                text = text.Slice(FramePrefix.Length);
            }
            int location = text.IndexOf(LocationSeparator, StringComparison.Ordinal);
            return location < 0 ? text : text.Slice(0, location);
        }

        private static StackFrame[] CreateFrames(string[] lines, int first, bool needFileInfo)
        {
            if (first >= lines.Length)
            {
                return [];
            }

            StackFrame[] frames = new StackFrame[lines.Length - first];
            for (int index = first; index < lines.Length; index++)
            {
                frames[index - first] = CreateFrame(lines[index], needFileInfo);
            }
            return frames;
        }

        private static StackFrame CreateFrame(string line, bool needFileInfo)
        {
            string text = line.StartsWith(FramePrefix, StringComparison.Ordinal)
                ? line.Substring(FramePrefix.Length)
                : line;
            int location = text.IndexOf(LocationSeparator, StringComparison.Ordinal);
            int lineMarker = location < 0
                ? -1
                : text.LastIndexOf(LineSeparator, StringComparison.Ordinal);
            if (lineMarker <= location ||
                !int.TryParse(text.AsSpan(lineMarker + LineSeparator.Length), out int lineNumber))
            {
                return new StackFrame(text, null, 0);
            }

            string method = text.Substring(0, location);
            if (!needFileInfo)
            {
                return new StackFrame(method, null, 0);
            }
            int document = location + LocationSeparator.Length;
            return new StackFrame(method, text.Substring(document, lineMarker - document), lineNumber);
        }

        private sealed class StackTraceCaptureException : Exception
        {
        }
    }
}
