// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace System.IO.Pipelines
{
    internal static class ThrowHelper
    {
        [DoesNotReturn]
        internal static void ThrowArgumentOutOfRangeException(ExceptionArgument argument) => throw CreateArgumentOutOfRangeException(argument);
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static ArgumentOutOfRangeException CreateArgumentOutOfRangeException(ExceptionArgument argument) => new ArgumentOutOfRangeException(argument.ToString());

        [DoesNotReturn]
        internal static void ThrowArgumentNullException(ExceptionArgument argument) => throw CreateArgumentNullException(argument);
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static ArgumentNullException CreateArgumentNullException(ExceptionArgument argument) => new ArgumentNullException(argument.ToString());

        [DoesNotReturn]
        public static void ThrowInvalidOperationException_AlreadyReading() => throw CreateInvalidOperationException_AlreadyReading();
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static InvalidOperationException CreateInvalidOperationException_AlreadyReading() => new InvalidOperationException("Reading is already in progress.");

        [DoesNotReturn]
        public static void ThrowInvalidOperationException_NoReadToComplete() => throw CreateInvalidOperationException_NoReadToComplete();
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static InvalidOperationException CreateInvalidOperationException_NoReadToComplete() => new InvalidOperationException("No reading operation to complete.");

        [DoesNotReturn]
        public static void ThrowInvalidOperationException_NoConcurrentOperation() => throw CreateInvalidOperationException_NoConcurrentOperation();
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static InvalidOperationException CreateInvalidOperationException_NoConcurrentOperation() => new InvalidOperationException("Concurrent reads or writes are not supported.");

        [DoesNotReturn]
        public static void ThrowInvalidOperationException_GetResultNotCompleted() => throw CreateInvalidOperationException_GetResultNotCompleted();
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static InvalidOperationException CreateInvalidOperationException_GetResultNotCompleted() => new InvalidOperationException("Can't GetResult unless awaiter is completed.");

        [DoesNotReturn]
        public static void ThrowInvalidOperationException_NoWritingAllowed() => throw CreateInvalidOperationException_NoWritingAllowed();

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static InvalidOperationException CreateInvalidOperationException_NoWritingAllowed() => new InvalidOperationException("Writing is not allowed after writer was completed.");

        [DoesNotReturn]
        public static void ThrowInvalidOperationException_NoReadingAllowed() => throw CreateInvalidOperationException_NoReadingAllowed();
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static InvalidOperationException CreateInvalidOperationException_NoReadingAllowed() => new InvalidOperationException("Reading is not allowed after reader was completed.");

        [DoesNotReturn]
        public static void ThrowInvalidOperationException_InvalidExaminedPosition() => throw CreateInvalidOperationException_InvalidExaminedPosition();
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static InvalidOperationException CreateInvalidOperationException_InvalidExaminedPosition() => new InvalidOperationException("The examined position cannot be less than the previously examined position.");

        [DoesNotReturn]
        public static void ThrowInvalidOperationException_InvalidExaminedOrConsumedPosition() => throw CreateInvalidOperationException_InvalidExaminedOrConsumedPosition();
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static InvalidOperationException CreateInvalidOperationException_InvalidExaminedOrConsumedPosition() => new InvalidOperationException("The examined position must be greater than or equal to the consumed position.");

        [DoesNotReturn]
        public static void ThrowInvalidOperationException_AdvanceToInvalidCursor() => throw CreateInvalidOperationException_AdvanceToInvalidCursor();
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static InvalidOperationException CreateInvalidOperationException_AdvanceToInvalidCursor() => new InvalidOperationException("The PipeReader has already advanced past the provided position.");

        [DoesNotReturn]
        public static void ThrowInvalidOperationException_ResetIncompleteReaderWriter() => throw CreateInvalidOperationException_ResetIncompleteReaderWriter();
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static InvalidOperationException CreateInvalidOperationException_ResetIncompleteReaderWriter() => new InvalidOperationException("Both reader and writer has to be completed to be able to reset the pipe.");

        [DoesNotReturn]
        public static void ThrowOperationCanceledException_ReadCanceled() => throw CreateOperationCanceledException_ReadCanceled();
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static OperationCanceledException CreateOperationCanceledException_ReadCanceled() => new OperationCanceledException("Read was canceled on underlying PipeReader.");

        [DoesNotReturn]
        public static void ThrowOperationCanceledException_FlushCanceled() => throw CreateOperationCanceledException_FlushCanceled();
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static OperationCanceledException CreateOperationCanceledException_FlushCanceled() => new OperationCanceledException("Flush was canceled on underlying PipeWriter.");

        [DoesNotReturn]
        public static void ThrowInvalidOperationException_InvalidZeroByteRead() => throw CreateInvalidOperationException_InvalidZeroByteRead();
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static InvalidOperationException CreateInvalidOperationException_InvalidZeroByteRead() => new InvalidOperationException("The PipeReader returned 0 bytes when the ReadResult was not completed or canceled.");

        [DoesNotReturn]
        public static void ThrowNotSupported_UnflushedBytes() => throw CreateNotSupportedException_UnflushedBytes();
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static NotSupportedException CreateNotSupportedException_UnflushedBytes() => new NotSupportedException("UnflushedBytes is not supported.");
    }

    internal enum ExceptionArgument
    {
        minimumSize,
        bytes,
        callback,
        options,
        pauseWriterThreshold,
        resumeWriterThreshold,
        sizeHint,
        destination,
        buffer,
        source,
        readingStream,
        writingStream,
    }
}
