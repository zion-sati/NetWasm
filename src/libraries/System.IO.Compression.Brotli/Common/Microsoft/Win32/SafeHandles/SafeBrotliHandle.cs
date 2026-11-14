// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Ported from dotnet/runtime commit deae2e32bf0e018e09d64597b7c59b62b73bb3ce.

using System;
using System.Runtime.InteropServices;

namespace Microsoft.Win32.SafeHandles
{
    internal sealed class SafeBrotliEncoderHandle : SafeHandle
    {
        public SafeBrotliEncoderHandle() : base(IntPtr.Zero, true) { }

        protected override bool ReleaseHandle()
        {
            Interop.Brotli.BrotliEncoderDestroyInstance(handle);
            return true;
        }

        public override bool IsInvalid => handle == IntPtr.Zero;
    }

    internal sealed class SafeBrotliDecoderHandle : SafeHandle
    {
        public SafeBrotliDecoderHandle() : base(IntPtr.Zero, true) { }

        protected override bool ReleaseHandle()
        {
            Interop.Brotli.BrotliDecoderDestroyInstance(handle);
            return true;
        }

        public override bool IsInvalid => handle == IntPtr.Zero;
    }
}
