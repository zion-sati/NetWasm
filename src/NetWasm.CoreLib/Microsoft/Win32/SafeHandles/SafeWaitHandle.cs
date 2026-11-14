// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Ported from dotnet/runtime commit deae2e32bf0e018e09d64597b7c59b62b73bb3ce.

using System;

namespace Microsoft.Win32.SafeHandles
{
    public sealed partial class SafeWaitHandle : SafeHandleZeroOrMinusOneIsInvalid
    {
        /// <summary>
        /// Creates a <see cref="T:Microsoft.Win32.SafeHandles.SafeWaitHandle" />.
        /// </summary>
        public SafeWaitHandle() : base(true)
        {
        }

        /// <summary>
        /// Creates a <see cref="T:Microsoft.Win32.SafeHandles.SafeWaitHandle" /> around a wait handle.
        /// </summary>
        /// <param name="existingHandle">Handle to wrap</param>
        /// <param name="ownsHandle">Whether to control the handle lifetime</param>
        public SafeWaitHandle(IntPtr existingHandle, bool ownsHandle) : base(ownsHandle)
        {
            SetHandle(existingHandle);
        }
    }
}
