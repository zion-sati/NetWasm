// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Ported from dotnet/runtime commit deae2e32bf0e018e09d64597b7c59b62b73bb3ce.

using System.Diagnostics;
using System.Runtime.InteropServices;

namespace System.IO.Compression
{
    internal static class Crc32Helper
    {
        // Calculate CRC based on the old CRC and the new bytes
        public static unsafe uint UpdateCrc32(uint crc32, byte[] buffer, int offset, int length)
        {
            Debug.Assert((buffer != null) && (offset >= 0) && (length >= 0) && (offset <= buffer.Length - length));
            fixed (byte* bufferPtr = &buffer[offset])
            {
                return Interop.ZLib.crc32(crc32, bufferPtr, length);
            }
        }

        public static unsafe uint UpdateCrc32(uint crc32, ReadOnlySpan<byte> buffer)
        {
            fixed (byte* bufferPtr = &MemoryMarshal.GetReference(buffer))
            {
                return Interop.ZLib.crc32(crc32, bufferPtr, buffer.Length);
            }
        }
    }
}
