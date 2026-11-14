// Adapted from dotnet/runtime NativeAOT System.Private.CoreLib's
// RuntimeExceptionHelpers.GetRuntimeException and PreallocatedOutOfMemoryException,
// src/coreclr/nativeaot/System.Private.CoreLib/src/System/RuntimeExceptionHelpers.cs,
// at commit deae2e32bf0e018e09d64597b7c59b62b73bb3ce.
// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Runtime.CompilerServices;

namespace System
{
    internal static class PreallocatedOutOfMemoryException
    {
        // NetWasm: frozen in the image, as upstream's GetRuntimeException note
        // asks for, instead of allocated by Initialize. The compiler makes it as
        // new OutOfMemoryException(message: null) would, its references are
        // roots, and the runtime records no stack trace into it, so every throw
        // can share it.
        public static OutOfMemoryException Instance => GetInstance();

        [MethodImpl(MethodImplOptions.InternalCall)]
        private static extern OutOfMemoryException GetInstance();
    }

    // NetWasm: GetRuntimeException's cases, one method each, so a program links
    // only the exceptions its checks can raise. A failed check passes the
    // compiler's managed.throw its exception's tag; managed.throw calls the
    // method for it and throws the exception the method returns.
    internal static class RuntimeExceptionHelpers
    {
        [ThreadStatic]
        private static bool t_allocatingOutOfMemoryException;

        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static Exception GetOutOfMemoryException()
        {
            Exception outOfMemoryException = PreallocatedOutOfMemoryException.Instance;

            // If possible, try to allocate proper out-of-memory exception with error message and stack trace
            if (!t_allocatingOutOfMemoryException)
            {
                t_allocatingOutOfMemoryException = true;
                try
                {
                    outOfMemoryException = new OutOfMemoryException();
                }
                catch
                {
                }
                t_allocatingOutOfMemoryException = false;
            }

            return outOfMemoryException;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static Exception GetArithmeticException() => new ArithmeticException();

        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static Exception GetArrayTypeMismatchException() => new ArrayTypeMismatchException();

        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static Exception GetDivideByZeroException() => new DivideByZeroException();

        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static Exception GetIndexOutOfRangeException() => new IndexOutOfRangeException();

        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static Exception GetInvalidCastException() => new InvalidCastException();

        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static Exception GetOverflowException() => new OverflowException();

        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static Exception GetNullReferenceException() => new NullReferenceException();

        // NetWasm: the compiler also checks arguments, operations and JavaScript
        // calls without a throw instruction.
        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static Exception GetArgumentException() => new ArgumentException();

        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static Exception GetArgumentNullException() => new ArgumentNullException();

        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static Exception GetArgumentOutOfRangeException() => new ArgumentOutOfRangeException();

        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static Exception GetInvalidOperationException() => new InvalidOperationException();

        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static Exception GetNotSupportedException() => new NotSupportedException();

        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static Exception GetJSException() => new JSException();
    }
}
