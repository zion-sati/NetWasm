// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// NetWasm platform layer for StackFrame, in place of dotnet/runtime commit
// deae2e32bf0e018e09d64597b7c59b62b73bb3ce StackFrame.CoreCLR.cs and StackFrame.NativeAot.cs.
// Like native AOT without reflection metadata, a frame keeps the method's display name rather
// than a MethodBase: the name, document and line NetWasm's opt-in managed stack trace reports
// (StackTraceCapture.NetWasm.cs). Without that instrumentation, building a frame throws
// PlatformNotSupportedException. GetMethod throws it in both cases, because it would need
// reflection's MethodBase. ToString is upstream's, with the display name in place of the method's
// name, as native AOT's AppendStackFrameWithoutMethodBase does.

using System.Reflection;
using System.Text;

namespace System.Diagnostics
{
    public partial class StackFrame
    {
        private string? _methodDisplayName;

        internal StackFrame(string methodDisplayName, string? fileName, int lineNumber)
        {
            InitMembers();
            _methodDisplayName = methodDisplayName;
            _fileName = fileName;
            _lineNumber = lineNumber;
        }

        private void BuildStackFrame(int skipFrames, bool needFileInfo)
        {
            StackFrame[] frames = StackTraceCapture.CaptureCurrentThread(needFileInfo) ??
                throw new PlatformNotSupportedException(SR.StackTraceNotSupported);
            if (skipFrames < frames.Length)
            {
                StackFrame frame = frames[skipFrames];
                _methodDisplayName = frame._methodDisplayName;
                _fileName = frame._fileName;
                _lineNumber = frame._lineNumber;
            }
        }

        public virtual MethodBase? GetMethod() =>
            throw new PlatformNotSupportedException(SR.StackTraceNotSupported);

        // Appends this frame to a stack trace in Exception.StackTrace's layout.
        internal bool AppendToStackTrace(StringBuilder builder, bool firstFrame)
        {
            if (_methodDisplayName is null)
            {
                return false;
            }

            if (!firstFrame)
            {
                builder.AppendLine();
            }
            builder.Append("at ").Append(_methodDisplayName);
            if (_fileName is not null)
            {
                builder.Append(" in ").Append(_fileName).Append(":line ").Append(_lineNumber);
            }
            return true;
        }

        /// <summary>
        /// Builds a readable representation of the stack frame
        /// </summary>
        public override string ToString()
        {
            StringBuilder sb = new StringBuilder(255);

            if (_methodDisplayName is not null)
            {
                sb.Append(_methodDisplayName);
                sb.Append(" at offset ");
                if (_nativeOffset == OFFSET_UNKNOWN)
                    sb.Append("<offset unknown>");
                else
                    sb.Append(_nativeOffset);

                sb.Append(" in file:line:column ");
                sb.Append(_fileName ?? "<filename unknown>");
                sb.Append(':');
                sb.Append(_lineNumber);
                sb.Append(':');
                sb.Append(_columnNumber);
            }
            else
            {
                sb.Append("<null>");
            }
            sb.AppendLine();

            return sb.ToString();
        }
    }
}
