// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Ported from dotnet/runtime commit deae2e32bf0e018e09d64597b7c59b62b73bb3ce.
// NetWasm adaptation: StackFrame.NetWasm.cs builds frames from NetWasm's opt-in managed stack
// trace, so without it every constructor throws PlatformNotSupportedException; GetMethod throws
// it in all cases. The stored-field accessors are upstream's. The rethrow-marker flag, which only
// the runtime's own frame builder sets, is omitted.

using System.Runtime.CompilerServices;

namespace System.Diagnostics
{
    /// <summary>
    /// There is no good reason for the methods of this class to be virtual.
    /// </summary>
    public partial class StackFrame
    {
        /// <summary>
        /// Native offset of the current instruction within the current method if available,
        /// OFFSET_UNKNOWN otherwise.
        /// </summary>
        private int _nativeOffset;

        /// <summary>
        /// IL offset of the current instruction within the current method if available,
        /// OFFSET_UNKNOWN otherwise.
        /// </summary>
        private int _ilOffset;

        /// <summary>
        /// Source file name representing the current code location if available, null otherwise.
        /// </summary>
        private string? _fileName;

        /// <summary>
        /// Line number representing the current code location if available, 0 otherwise.
        /// </summary>
        private int _lineNumber;

        /// <summary>
        /// Column number representing the current code location if available, 0 otherwise.
        /// </summary>
        private int _columnNumber;

        private void InitMembers()
        {
            _nativeOffset = OFFSET_UNKNOWN;
            _ilOffset = OFFSET_UNKNOWN;
        }

        /// <summary>
        /// Constructs a StackFrame corresponding to the active stack frame.
        /// </summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        public StackFrame()
        {
            InitMembers();
            BuildStackFrame(StackTrace.METHODS_TO_SKIP, false);
        }

        /// <summary>
        /// Constructs a StackFrame corresponding to the active stack frame.
        /// </summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        public StackFrame(bool needFileInfo)
        {
            InitMembers();
            BuildStackFrame(StackTrace.METHODS_TO_SKIP, needFileInfo);
        }

        /// <summary>
        /// Constructs a StackFrame corresponding to a calling stack frame.
        /// </summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        public StackFrame(int skipFrames)
        {
            InitMembers();
            BuildStackFrame(skipFrames + StackTrace.METHODS_TO_SKIP, false);
        }

        /// <summary>
        /// Constructs a StackFrame corresponding to a calling stack frame.
        /// </summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        public StackFrame(int skipFrames, bool needFileInfo)
        {
            InitMembers();
            BuildStackFrame(skipFrames + StackTrace.METHODS_TO_SKIP, needFileInfo);
        }

        /// <summary>
        /// Constructs a "fake" stack frame, just containing the given file
        /// name and line number.  Use when you don't want to use the
        /// debugger's line mapping logic.
        /// </summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        public StackFrame(string? fileName, int lineNumber)
        {
            InitMembers();

            BuildStackFrame(StackTrace.METHODS_TO_SKIP, false);
            _fileName = fileName;
            _lineNumber = lineNumber;
        }

        /// <summary>
        /// Constructs a "fake" stack frame, just containing the given file
        /// name, line number and column number.  Use when you don't want to
        /// use the debugger's line mapping logic.
        /// </summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        public StackFrame(string? fileName, int lineNumber, int colNumber)
        {
            InitMembers();

            BuildStackFrame(StackTrace.METHODS_TO_SKIP, false);
            _fileName = fileName;
            _lineNumber = lineNumber;
            _columnNumber = colNumber;
        }

        /// <summary>
        /// Constant returned when the native or IL offset is unknown
        /// </summary>
        public const int OFFSET_UNKNOWN = -1;

        /// <summary>
        /// Returns the offset from the start of the native (jitted) code for the
        /// method being executed
        /// </summary>
        public virtual int GetNativeOffset()
        {
            return _nativeOffset;
        }

        /// <summary>
        /// Returns the offset from the start of the IL code for the
        /// method being executed.  This offset may be approximate depending
        /// on whether the jitter is generating debuggable code or not.
        /// </summary>
        public virtual int GetILOffset()
        {
            return _ilOffset;
        }

        /// <summary>
        /// Returns the file name containing the code being executed.  This
        /// information is normally extracted from the debugging symbols
        /// for the executable.
        /// </summary>
        public virtual string? GetFileName()
        {
            return _fileName;
        }

        /// <summary>
        /// Returns the line number in the file containing the code being executed.
        /// This information is normally extracted from the debugging symbols
        /// for the executable.
        /// </summary>
        public virtual int GetFileLineNumber()
        {
            return _lineNumber;
        }

        /// <summary>
        /// Returns the column number in the line containing the code being executed.
        /// This information is normally extracted from the debugging symbols
        /// for the executable.
        /// </summary>
        public virtual int GetFileColumnNumber()
        {
            return _columnNumber;
        }
    }
}
