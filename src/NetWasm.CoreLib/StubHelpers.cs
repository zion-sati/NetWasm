// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: MIT

// Portions adapted from dotnet/runtime System.Private.CoreLib StubHelpers at
// commit deae2e32bf0e018e09d64597b7c59b62b73bb3ce.
// The upstream implementation is licensed under the MIT license.

using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace System;

// The helpers the compiler's P/Invoke marshalling stubs call, as desktop .NET's IL stubs call its StubHelpers: a
// SafeHandle argument is borrowed for the call and released after it, and an ANSI char converts as AnsiCharMarshaler
// converts it.
internal static class StubHelpers
{
    // AddRefs the SH and returns the underlying unmanaged handle.
    internal static IntPtr SafeHandleAddRef(SafeHandle pHandle, ref bool success)
    {
        if (pHandle == null)
        {
            ThrowHelper.ThrowArgumentNullException(ExceptionArgument.pHandle, ExceptionResource.ArgumentNull_SafeHandle);
        }

        pHandle.DangerousAddRef(ref success);
        return pHandle.DangerousGetHandle();
    }

    // AnsiCharMarshaler.ConvertToNative: the first byte of the character's ANSI encoding, UTF-8 on Unix.
    internal static unsafe byte AnsiCharToNative(char managedChar, bool fBestFit, bool fThrowOnUnmappableChar)
    {
        int cbAllocLength = (1 + 1) * Marshal.SystemMaxDBCSCharSize;
        Debug.Assert(cbAllocLength <= 512); // Some arbitrary upper limit, in most cases SystemMaxDBCSCharSize is expected to be 1 or 2.
        byte* bufferPtr = stackalloc byte[cbAllocLength];

        int cbLength = Marshal.StringToAnsiString(managedChar.ToString(), bufferPtr, cbAllocLength, fBestFit, fThrowOnUnmappableChar);

        Debug.Assert(cbLength > 0, "Zero bytes returned from DoAnsiConversion in AnsiCharMarshaler.ConvertToNative");
        return bufferPtr[0];
    }

    // AnsiCharMarshaler.ConvertToManaged: the character the byte decodes to.
    internal static char AnsiCharToManaged(byte nativeChar)
    {
        var bytes = new ReadOnlySpan<byte>(in nativeChar);
        string str = Encoding.Default.GetString(bytes);
        return str[0];
    }

    // Releases the SH (to be called from finally block).
    internal static void SafeHandleRelease(SafeHandle pHandle)
    {
        if (pHandle == null)
        {
            ThrowHelper.ThrowArgumentNullException(ExceptionArgument.pHandle, ExceptionResource.ArgumentNull_SafeHandle);
        }

        pHandle.DangerousRelease();
    }
}
