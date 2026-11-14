// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Ported from dotnet/runtime deae2e32bf0e018e09d64597b7c59b62b73bb3ce (Marshal.Mono.cs).
// NetWasm counterpart of upstream's runtime-specific Marshal part. The last P/Invoke error is native AOT's.
// NetWasm prelinks nothing at run time and generates no delegate trampolines, so those services throw
// PlatformNotSupportedException; the object-based reads and writes and
// GetExceptionCode follow Mono's PlatformNotSupported region verbatim.

using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace System.Runtime.InteropServices
{
    public static partial class Marshal
    {
        // A pinned GC handle may not expose an object that holds references.
        internal static bool IsPinnable(object? obj) =>
            obj is null || !RuntimeHelpers.ObjectContainsReferences(obj);

        // Native AOT's (PInvokeMarshal.t_lastError): the value LibraryImport's generated code stores after a call with
        // SetLastError. A thread-static there; NetWasm has one thread.
        private static int s_lastPInvokeError;

        public static int GetLastPInvokeError() => s_lastPInvokeError;

        public static void SetLastPInvokeError(int error) => s_lastPInvokeError = error;

        // Upstream's Unix GetPInvokeErrorMessage, internal here (it is newer than netstandard 2.1): the C library's
        // message for the error, from NetWasm's table of the WASI C library's messages.
        internal static string GetPInvokeErrorMessage(int error) => Interop.Sys.StrError(error);

        private static void PrelinkCore(MethodInfo m) =>
            throw new PlatformNotSupportedException(SR.PlatformNotSupported_Prelink);

        #region PlatformNotSupported

        [EditorBrowsable(EditorBrowsableState.Never)]
        [Obsolete("GetExceptionCode() may be unavailable in future releases.")]
        public static int GetExceptionCode()
        {
            throw new PlatformNotSupportedException();
        }

        [EditorBrowsable(EditorBrowsableState.Never)]
        [Obsolete("ReadByte(Object, Int32) may be unavailable in future releases.")]
        [RequiresDynamicCode("Marshalling code for the object might not be available")]
        public static byte ReadByte(object ptr, int ofs)
        {
            throw new PlatformNotSupportedException();
        }

        [EditorBrowsable(EditorBrowsableState.Never)]
        [Obsolete("ReadInt16(Object, Int32) may be unavailable in future releases.")]
        [RequiresDynamicCode("Marshalling code for the object might not be available")]
        public static short ReadInt16(object ptr, int ofs)
        {
            throw new PlatformNotSupportedException();
        }

        [EditorBrowsable(EditorBrowsableState.Never)]
        [Obsolete("ReadInt32(Object, Int32) may be unavailable in future releases.")]
        [RequiresDynamicCode("Marshalling code for the object might not be available")]
        public static int ReadInt32(object ptr, int ofs)
        {
            throw new PlatformNotSupportedException();
        }

        [EditorBrowsable(EditorBrowsableState.Never)]
        [Obsolete("ReadInt64(Object, Int32) may be unavailable in future releases.")]
        [RequiresDynamicCode("Marshalling code for the object might not be available")]
        public static long ReadInt64(object ptr, int ofs)
        {
            throw new PlatformNotSupportedException();
        }

        [EditorBrowsable(EditorBrowsableState.Never)]
        [Obsolete("WriteByte(Object, Int32, Byte) may be unavailable in future releases.")]
        [RequiresDynamicCode("Marshalling code for the object might not be available")]
        public static void WriteByte(object ptr, int ofs, byte val)
        {
            throw new PlatformNotSupportedException();
        }

        [EditorBrowsable(EditorBrowsableState.Never)]
        [Obsolete("WriteInt16(Object, Int32, Int16) may be unavailable in future releases.")]
        [RequiresDynamicCode("Marshalling code for the object might not be available")]
        public static void WriteInt16(object ptr, int ofs, short val)
        {
            throw new PlatformNotSupportedException();
        }

        [EditorBrowsable(EditorBrowsableState.Never)]
        [Obsolete("WriteInt32(Object, Int32, Int32) may be unavailable in future releases.")]
        [RequiresDynamicCode("Marshalling code for the object might not be available")]
        public static void WriteInt32(object ptr, int ofs, int val)
        {
            throw new PlatformNotSupportedException();
        }

        [EditorBrowsable(EditorBrowsableState.Never)]
        [Obsolete("WriteInt64(Object, Int32, Int64) may be unavailable in future releases.")]
        [RequiresDynamicCode("Marshalling code for the object might not be available")]
        public static void WriteInt64(object ptr, int ofs, long val)
        {
            throw new PlatformNotSupportedException();
        }

        #endregion
    }
}
