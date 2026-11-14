// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// NetWasm platform layer for StackTrace, in place of dotnet/runtime commit
// deae2e32bf0e018e09d64597b7c59b62b73bb3ce StackTrace.CoreCLR.cs and StackTrace.NativeAot.cs.
// Frames come from NetWasm's opt-in managed stack trace (StackTraceCapture.NetWasm.cs); without
// it, capturing the current thread's or an exception's frames throws
// PlatformNotSupportedException rather than produce fabricated frames. An exception's frames are
// the ones its Exception.StackTrace reports: NetWasm captures the whole managed stack at the
// throw. ToString keeps upstream's layout (one frame per line, trailing new line) but writes each
// frame as Exception.StackTrace does ("at <method>" or "at <method> in <file>:line <n>").

using System.Text;

namespace System.Diagnostics
{
    public partial class StackTrace
    {
        private void InitializeForCurrentThread(int skipFrames, bool needFileInfo) =>
            InitializeForFrames(
                StackTraceCapture.CaptureCurrentThread(needFileInfo) ??
                    throw new PlatformNotSupportedException(SR.StackTraceNotSupported),
                skipFrames);

        private void InitializeForException(Exception exception, int skipFrames, bool needFileInfo) =>
            InitializeForFrames(
                StackTraceCapture.CaptureException(exception, needFileInfo) ??
                    throw new PlatformNotSupportedException(SR.StackTraceNotSupported),
                skipFrames);

        private void InitializeForFrames(StackFrame[] frames, int skipFrames)
        {
            _stackFrames = frames;
            _methodsToSkip = Math.Min(skipFrames, frames.Length);
            _numOfFrames = frames.Length - _methodsToSkip;
        }

        /// <summary>
        /// Builds a readable representation of the stack trace
        /// </summary>
        public override string ToString()
        {
            StringBuilder sb = new StringBuilder(255);
            bool fFirstFrame = true;
            for (int iFrameIndex = 0; iFrameIndex < _numOfFrames; iFrameIndex++)
            {
                StackFrame? sf = GetFrame(iFrameIndex);
                if (sf is null || !sf.AppendToStackTrace(sb, fFirstFrame))
                {
                    continue;
                }
                fFirstFrame = false;
            }
            sb.AppendLine();
            return sb.ToString();
        }
    }
}
