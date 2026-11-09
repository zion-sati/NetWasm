// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Ported from dotnet/runtime 474ed963699fc29809015392738a15231aaae2de
// (Marshal.Unix.cs). NetWasm adaptation: Interop.Sys.Malloc/Free map to NativeMemory, and ANSI
// means UTF-8 as on Unix. The allocation entry points live in Marshal.cs, and errno-based
// last-system-error members are omitted because NetWasm keeps no errno.

using System.Diagnostics;
using System.Text;

namespace System.Runtime.InteropServices
{
    public static unsafe partial class Marshal
    {
        public static string? PtrToStringAuto(IntPtr ptr, int len)
        {
            return PtrToStringUTF8(ptr, len);
        }

        public static string? PtrToStringAuto(IntPtr ptr)
        {
            return PtrToStringUTF8(ptr);
        }

        public static IntPtr StringToHGlobalAuto(string? s)
        {
            return StringToHGlobalUTF8(s);
        }

        public static IntPtr StringToCoTaskMemAuto(string? s)
        {
            return StringToCoTaskMemUTF8(s);
        }

        private static bool IsNullOrWin32Atom(IntPtr ptr) => ptr == IntPtr.Zero;

        internal static unsafe int StringToAnsiString(string s, byte* buffer, int bufferLength, bool bestFit = false, bool throwOnUnmappableChar = false)
        {
            Debug.Assert(bufferLength >= (s.Length + 1) * SystemMaxDBCSCharSize, "Insufficient buffer length passed to StringToAnsiString");

            int convertedBytes = Encoding.UTF8.GetBytes(s, new Span<byte>(buffer, bufferLength));
            buffer[convertedBytes] = 0;

            return convertedBytes;
        }

        public static unsafe IntPtr ReAllocHGlobal(IntPtr pv, nint cb)
        {
            return (nint)NativeMemory.Realloc((void*)(nint)pv, (nuint)cb);
        }

        public static unsafe IntPtr ReAllocCoTaskMem(IntPtr pv, int cb)
        {
            nuint cbNative = (nuint)(uint)cb;
            void* pvNative = (void*)(nint)pv;

            if ((cbNative == 0) && (pvNative != null))
            {
                NativeMemory.Free(pvNative);
                return IntPtr.Zero;
            }

            return (nint)NativeMemory.Realloc((void*)(nint)pv, cbNative);
        }

        internal static unsafe IntPtr AllocBSTRByteLen(uint length)
        {
            // SysAllocString on Windows aligns the memory block size up
            const nuint WIN32_ALLOC_ALIGN = 15;

            ulong cbNative = (ulong)(uint)length + (uint)sizeof(IntPtr) + (uint)sizeof(char) + WIN32_ALLOC_ALIGN;

            if (cbNative > uint.MaxValue)
            {
                throw new OutOfMemoryException();
            }

            void* p = NativeMemory.Alloc((nuint)cbNative & ~WIN32_ALLOC_ALIGN);

            if (p == null)
            {
                throw new OutOfMemoryException();
            }

            void* s = (byte*)p + sizeof(nuint);
            *(((uint*)s) - 1) = (uint)length;

            // NULL-terminate with both a narrow and wide zero.
            *(byte*)((byte*)s + length) = (byte)'\0';
            *(short*)((byte*)s + ((length + 1) & ~1)) = 0;

            return (nint)s;
        }
        // Returns number of bytes required to convert given string to Ansi string. The return value includes null terminator.
        internal static int GetAnsiStringByteCount(ReadOnlySpan<char> chars)
        {
            int byteLength = Encoding.UTF8.GetByteCount(chars);
            return checked(byteLength + 1);
        }

        // Converts given string to Ansi string. The destination buffer must be large enough to hold the converted value, including null terminator.
        internal static void GetAnsiStringBytes(ReadOnlySpan<char> chars, Span<byte> bytes)
        {
            int actualByteLength = Encoding.UTF8.GetBytes(chars, bytes);
            bytes[actualByteLength] = 0;
        }

        internal static IntPtr AllocBSTR(int length)
        {
            // SysAllocString on Windows aligns the memory block size up
            const nuint WIN32_ALLOC_ALIGN = 15;

            ulong cbNative = 2 * (ulong)(uint)length + (uint)sizeof(IntPtr) + (uint)sizeof(char) + WIN32_ALLOC_ALIGN;

            if (cbNative > uint.MaxValue)
            {
                throw new OutOfMemoryException();
            }

            void* p = NativeMemory.Alloc((nuint)cbNative & ~WIN32_ALLOC_ALIGN);

            void* s = (byte*)p + sizeof(nuint);
            *(((uint*)s) - 1) = (uint)(length * sizeof(char));
            ((char*)s)[length] = '\0';

            return (nint)s;
        }

        public static void FreeBSTR(IntPtr ptr)
        {
            void* ptrNative = (void*)(nint)ptr;

            if (ptrNative != null)
            {
                NativeMemory.Free((byte*)ptr - sizeof(nuint));
            }
        }
    }
}
