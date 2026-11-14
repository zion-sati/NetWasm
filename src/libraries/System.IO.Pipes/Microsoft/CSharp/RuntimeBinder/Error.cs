// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Ported from dotnet/runtime commit deae2e32bf0e018e09d64597b7c59b62b73bb3ce.
// NetWasm: declared in System.IO.Pipes rather than System.IO so that it uses the pipes SR strings.

namespace System.IO.Pipes
{
    // Only static data; no need to serialize
    internal static class Error
    {
        internal static Exception GetEndOfFile()
        {
            return new EndOfStreamException(SR.IO_EOF_ReadBeyondEOF);
        }

        internal static Exception GetPipeNotOpen()
        {
            return new ObjectDisposedException(null, SR.ObjectDisposed_PipeClosed);
        }

        internal static Exception GetReadNotSupported()
        {
            return new NotSupportedException(SR.NotSupported_UnreadableStream);
        }

        internal static Exception GetSeekNotSupported()
        {
            return new NotSupportedException(SR.NotSupported_UnseekableStream);
        }

        internal static Exception GetWriteNotSupported()
        {
            return new NotSupportedException(SR.NotSupported_UnwritableStream);
        }

        internal static Exception GetOperationAborted()
        {
            return new IOException(SR.IO_OperationAborted_Unexpected);
        }
    }
}
