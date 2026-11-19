// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// The contract of dotnet/runtime commit deae2e32bf0e018e09d64597b7c59b62b73bb3ce SafeProcessHandle.cs.
// Concrete process handles are a deferred platform resource (API_FAMILY_REVIEW.md "Concrete platform
// handles"): NetWasm has no guest process-handle ABI, so every constructor rejects its handle, including
// invalid and non-owning ones, and no handle is ever released. Open, TryOpen and Start make upstream's
// argument checks and then reject the platform, as upstream does where processes cannot be started or
// killed. With no instance possible, the instance operations only state that.

using System;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Win32.SafeHandles
{
    public sealed partial class SafeProcessHandle : SafeHandleZeroOrMinusOneIsInvalid
    {
        /// <summary>
        /// Creates a <see cref="T:Microsoft.Win32.SafeHandles.SafeHandle" />.
        /// </summary>
        public SafeProcessHandle()
            : this(IntPtr.Zero, ownsHandle: true)
        {
        }

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

        public int ProcessId =>
            throw new PlatformNotSupportedException(System.SR.ProcessHandleNotSupported);

        [UnsupportedOSPlatform("ios")]
        [UnsupportedOSPlatform("tvos")]
        [SupportedOSPlatform("maccatalyst")]
        public static SafeProcessHandle Open(int processId)
        {
            ValidateOpenArguments(processId);
            throw new PlatformNotSupportedException(System.SR.ProcessHostNotSupported);
        }

        [UnsupportedOSPlatform("ios")]
        [UnsupportedOSPlatform("tvos")]
        [SupportedOSPlatform("maccatalyst")]
        public static bool TryOpen(int processId, [NotNullWhen(true)] out SafeProcessHandle? processHandle)
        {
            ValidateOpenArguments(processId);
            throw new PlatformNotSupportedException(System.SR.ProcessHostNotSupported);
        }

        private static void ValidateOpenArguments(int processId)
        {
            ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(processId, 0);
            throw new PlatformNotSupportedException(System.SR.ProcessHostNotSupported);
        }

        [UnsupportedOSPlatform("ios")]
        [UnsupportedOSPlatform("tvos")]
        [SupportedOSPlatform("maccatalyst")]
        public static SafeProcessHandle Start(ProcessStartInfo startInfo)
        {
            ArgumentNullException.ThrowIfNull(startInfo);

            return Start(startInfo, fallbackToNull: startInfo.StartDetached);
        }

        // Upstream's fallbackToNull chooses the null device for standard handles a started process lacks.
        [UnsupportedOSPlatform("ios")]
        [UnsupportedOSPlatform("tvos")]
        [SupportedOSPlatform("maccatalyst")]
        internal static SafeProcessHandle Start(ProcessStartInfo startInfo, bool fallbackToNull)
        {
            startInfo.ThrowIfInvalid(out bool anyRedirection, out _);

            if (anyRedirection)
            {
                throw new InvalidOperationException(System.SR.CantSetRedirectForSafeProcessHandleStart);
            }

            throw new PlatformNotSupportedException(System.SR.ProcessHostNotSupported);
        }

        [SupportedOSPlatform("windows")]
        [SupportedOSPlatform("macos")]
        public void Resume() => throw new PlatformNotSupportedException(System.SR.ProcessHandleNotSupported);

        [UnsupportedOSPlatform("ios")]
        [UnsupportedOSPlatform("tvos")]
        [SupportedOSPlatform("maccatalyst")]
        public void Kill() => throw new PlatformNotSupportedException(System.SR.ProcessHandleNotSupported);

        [UnsupportedOSPlatform("ios")]
        [UnsupportedOSPlatform("tvos")]
        [SupportedOSPlatform("maccatalyst")]
        public bool Signal(PosixSignal signal) => throw new PlatformNotSupportedException(System.SR.ProcessHandleNotSupported);

        public ProcessExitStatus WaitForExit() => throw new PlatformNotSupportedException(System.SR.ProcessHandleNotSupported);

        public bool TryWaitForExit(TimeSpan timeout, [NotNullWhen(true)] out ProcessExitStatus? exitStatus) =>
            throw new PlatformNotSupportedException(System.SR.ProcessHandleNotSupported);

        [UnsupportedOSPlatform("ios")]
        [UnsupportedOSPlatform("tvos")]
        [SupportedOSPlatform("maccatalyst")]
        public ProcessExitStatus WaitForExitOrKillOnTimeout(TimeSpan timeout) =>
            throw new PlatformNotSupportedException(System.SR.ProcessHandleNotSupported);

        public Task<ProcessExitStatus> WaitForExitAsync(CancellationToken cancellationToken = default) =>
            Task.FromException<ProcessExitStatus>(new PlatformNotSupportedException(System.SR.ProcessHandleNotSupported));

        [UnsupportedOSPlatform("ios")]
        [UnsupportedOSPlatform("tvos")]
        [SupportedOSPlatform("maccatalyst")]
        public Task<ProcessExitStatus> WaitForExitOrKillOnCancellationAsync(CancellationToken cancellationToken) =>
            Task.FromException<ProcessExitStatus>(new PlatformNotSupportedException(System.SR.ProcessHandleNotSupported));

        protected override bool ReleaseHandle() =>
            throw new PlatformNotSupportedException(System.SR.ProcessHandleNotSupported);
    }
}
