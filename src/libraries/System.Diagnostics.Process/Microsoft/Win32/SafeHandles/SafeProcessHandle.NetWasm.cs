// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// The .NET Standard 2.1 contract of dotnet/runtime commit deae2e32bf0e018e09d64597b7c59b62b73bb3ce
// SafeProcessHandle.cs. Concrete process handles are a deferred platform resource
// (API_FAMILY_REVIEW.md "Concrete platform handles"): NetWasm has no guest process-handle ABI, so
// the constructor rejects every handle, including invalid and non-owning ones, and no handle is
// ever released. The newer process-handle operations are not ported.

using System;

namespace Microsoft.Win32.SafeHandles
{
    public sealed partial class SafeProcessHandle : SafeHandleZeroOrMinusOneIsInvalid
    {
        /// <summary>
        /// Creates a <see cref="T:Microsoft.Win32.SafeHandles.SafeHandle" /> around a process handle.
        /// </summary>
        /// <param name="existingHandle">Handle to wrap</param>
        /// <param name="ownsHandle">Whether to control the handle lifetime</param>
        public SafeProcessHandle(IntPtr existingHandle, bool ownsHandle)
            : base(ownsHandle)
        {
            // The rejected wrapper holds no handle, so it must not reach finalization.
            GC.SuppressFinalize(this);
            throw new PlatformNotSupportedException(System.SR.ProcessHandleNotSupported);
        }

        internal int ProcessId =>
            throw new PlatformNotSupportedException(System.SR.ProcessHandleNotSupported);

        protected override bool ReleaseHandle() =>
            throw new PlatformNotSupportedException(System.SR.ProcessHandleNotSupported);
    }
}
