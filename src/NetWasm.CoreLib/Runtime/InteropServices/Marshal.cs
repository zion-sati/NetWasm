// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Ported from dotnet/runtime commit 811225a482702af7ecc35d817966bc70b88a3a23.

using System.Text;

namespace System.Runtime.InteropServices
{
    public static unsafe class Marshal
    {
        // Keep these values local: this file is also compiled by the native-interop
        // support fixture without the rest of NetWasm.CoreLib.
        private const int ErrorArgument = unchecked((int)0x80070057);
        private const int ErrorArgumentOutOfRange = unchecked((int)0x80131502);
        private const int ErrorArithmetic = unchecked((int)0x80070216);
        private const int ErrorArrayTypeMismatch = unchecked((int)0x80131503);
        private const int ErrorDivideByZero = unchecked((int)0x80020012);
        private const int ErrorFormat = unchecked((int)0x80131537);
        private const int ErrorIndexOutOfRange = unchecked((int)0x80131508);
        private const int ErrorInvalidCast = unchecked((int)0x80004002);
        private const int ErrorInvalidOperation = unchecked((int)0x80131509);
        private const int ErrorKeyNotFound = unchecked((int)0x80131577);
        private const int ErrorNotSupported = unchecked((int)0x80131515);
        private const int ErrorObjectDisposed = unchecked((int)0x80131622);
        private const int ErrorOperationCanceled = unchecked((int)0x8013153B);
        private const int ErrorOutOfMemory = unchecked((int)0x8007000E);
        private const int ErrorOverflow = unchecked((int)0x80131516);
        private const int ErrorPlatformNotSupported = unchecked((int)0x80131539);
        private const int ErrorPointer = unchecked((int)0x80004003);

        public static IntPtr AllocHGlobal(int cb) => AllocHGlobal((nint)cb);

        public static IntPtr AllocHGlobal(nint cb) =>
            (nint)NativeMemory.Alloc((nuint)cb);

        public static void FreeHGlobal(IntPtr hglobal) =>
            NativeMemory.Free((void*)(nint)hglobal);

        public static IntPtr AllocCoTaskMem(int cb) =>
            AllocHGlobal((nint)(uint)cb);

        public static void FreeCoTaskMem(IntPtr ptr) =>
            FreeHGlobal(ptr);

        public static void ThrowExceptionForHR(int errorCode)
        {
            if (errorCode >= 0)
            {
                return;
            }

            Exception exception = errorCode switch
            {
                unchecked((int)0x80004001) => new NotImplementedException(),
                unchecked((int)0x80070005) => new UnauthorizedAccessException(),
                unchecked((int)0x8007000B) => new BadImageFormatException(),
                ErrorArgument => new ArgumentException(),
                ErrorArgumentOutOfRange => new ArgumentOutOfRangeException(),
                ErrorArithmetic => new ArithmeticException(),
                ErrorArrayTypeMismatch => new ArrayTypeMismatchException(),
                ErrorDivideByZero => new DivideByZeroException(),
                ErrorFormat => new FormatException(),
                ErrorIndexOutOfRange => new IndexOutOfRangeException(),
                ErrorInvalidCast => new InvalidCastException(),
                ErrorInvalidOperation => new InvalidOperationException(),
                ErrorKeyNotFound =>
                    new Collections.Generic.KeyNotFoundException(),
                ErrorNotSupported => new NotSupportedException(),
                ErrorObjectDisposed => new ObjectDisposedException(null),
                ErrorOperationCanceled => new OperationCanceledException(),
                ErrorOutOfMemory => new OutOfMemoryException(),
                ErrorOverflow => new OverflowException(),
                ErrorPlatformNotSupported =>
                    new PlatformNotSupportedException(),
                ErrorPointer => new NullReferenceException(),
                _ => new COMException(
                    "Error " + (uint)errorCode + " (0x" +
                        ((uint)errorCode).ToString("X8") + ")",
                    errorCode),
            };
            exception.HResult = errorCode;
            throw exception;
        }

        public static string? PtrToStringUTF8(IntPtr ptr)
        {
            if (ptr == IntPtr.Zero)
            {
                return null;
            }

            return Encoding.UTF8.GetString(
                MemoryMarshal.CreateReadOnlySpanFromNullTerminated((byte*)ptr));
        }

        public static string PtrToStringUTF8(IntPtr ptr, int byteLen)
        {
            if (ptr == IntPtr.Zero)
            {
                throw new ArgumentNullException(nameof(ptr));
            }
            ArgumentOutOfRangeException.ThrowIfNegative(byteLen);
            return Encoding.UTF8.GetString(new ReadOnlySpan<byte>((byte*)ptr, byteLen));
        }
    }
}
