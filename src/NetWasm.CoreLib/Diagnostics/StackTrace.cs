// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Ported from dotnet/runtime commit deae2e32bf0e018e09d64597b7c59b62b73bb3ce.
// NetWasm adaptation: the capture and exception constructors keep upstream's argument checks;
// StackTrace.NetWasm.cs reads their frames from NetWasm's opt-in managed stack trace and throws
// PlatformNotSupportedException without it, and supplies ToString. StackTrace(StackFrame) and
// the frame accessors are upstream's. The IEnumerable<StackFrame> constructor (outside .NET
// Standard 2.1), the feature switches and upstream's MethodBase-based trace formatter are
// omitted.

using System.Runtime.CompilerServices;

namespace System.Diagnostics
{
    /// <summary>
    /// Class which represents a description of a stack trace
    /// There is no good reason for the methods of this class to be virtual.
    /// </summary>
    public partial class StackTrace
    {
        public const int METHODS_TO_SKIP = 0;

        private int _numOfFrames;
        private int _methodsToSkip;

        /// <summary>
        /// Stack frames comprising this stack trace.
        /// </summary>
        private StackFrame[]? _stackFrames;

        /// <summary>
        /// Constructs a stack trace from the current location.
        /// </summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        public StackTrace()
        {
            InitializeForCurrentThread(METHODS_TO_SKIP, false);
        }

        /// <summary>
        /// Constructs a stack trace from the current location.
        /// </summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        public StackTrace(bool fNeedFileInfo)
        {
            InitializeForCurrentThread(METHODS_TO_SKIP, fNeedFileInfo);
        }

        /// <summary>
        /// Constructs a stack trace from the current location, in a caller's
        /// frame
        /// </summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        public StackTrace(int skipFrames)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(skipFrames);

            InitializeForCurrentThread(skipFrames + METHODS_TO_SKIP, false);
        }

        /// <summary>
        /// Constructs a stack trace from the current location, in a caller's
        /// frame
        /// </summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        public StackTrace(int skipFrames, bool fNeedFileInfo)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(skipFrames);

            InitializeForCurrentThread(skipFrames + METHODS_TO_SKIP, fNeedFileInfo);
        }

        /// <summary>
        /// Constructs a stack trace from the current location.
        /// </summary>
        public StackTrace(Exception e)
        {
            ArgumentNullException.ThrowIfNull(e);

            InitializeForException(e, METHODS_TO_SKIP, false);
        }

        /// <summary>
        /// Constructs a stack trace from the current location.
        /// </summary>
        public StackTrace(Exception e, bool fNeedFileInfo)
        {
            ArgumentNullException.ThrowIfNull(e);

            InitializeForException(e, METHODS_TO_SKIP, fNeedFileInfo);
        }

        /// <summary>
        /// Constructs a stack trace from the current location, in a caller's
        /// frame
        /// </summary>
        public StackTrace(Exception e, int skipFrames)
        {
            ArgumentNullException.ThrowIfNull(e);

            ArgumentOutOfRangeException.ThrowIfNegative(skipFrames);

            InitializeForException(e, skipFrames + METHODS_TO_SKIP, false);
        }

        /// <summary>
        /// Constructs a stack trace from the current location, in a caller's
        /// frame
        /// </summary>
        public StackTrace(Exception e, int skipFrames, bool fNeedFileInfo)
        {
            ArgumentNullException.ThrowIfNull(e);

            ArgumentOutOfRangeException.ThrowIfNegative(skipFrames);

            InitializeForException(e, skipFrames + METHODS_TO_SKIP, fNeedFileInfo);
        }

        /// <summary>
        /// Constructs a "fake" stack trace, just containing a single frame.
        /// Does not have the overhead of a full stack trace.
        /// </summary>
        public StackTrace(StackFrame frame)
        {
            _stackFrames = [frame];
            _numOfFrames = 1;
        }

        /// <summary>
        /// Property to get the number of frames in the stack trace
        /// </summary>
        public virtual int FrameCount => _numOfFrames;

        /// <summary>
        /// Returns a given stack frame.  Stack frames are numbered starting at
        /// zero, which is the last stack frame pushed.
        /// </summary>
        public virtual StackFrame? GetFrame(int index)
        {
            if (_stackFrames != null && index < _numOfFrames && index >= 0)
                return _stackFrames[index + _methodsToSkip];

            return null;
        }

        /// <summary>
        /// Returns an array of all stack frames for this stacktrace.
        /// The array is ordered and sized such that GetFrames()[i] == GetFrame(i)
        /// The nth element of this array is the same as GetFrame(n).
        /// The length of the array is the same as FrameCount.
        /// </summary>
        public virtual StackFrame[] GetFrames()
        {
            if (_stackFrames == null || _numOfFrames <= 0)
                return [];

            // We have to return a subset of the array. Unfortunately this
            // means we have to allocate a new array and copy over.
            StackFrame[] array = new StackFrame[_numOfFrames];
            Array.Copy(_stackFrames, _methodsToSkip, array, 0, _numOfFrames);
            return array;
        }
    }
}
