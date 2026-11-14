// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// NetWasm platform layer for Process, following dotnet/runtime commit
// deae2e32bf0e018e09d64597b7c59b62b73bb3ce Process.Unix.cs, Process.Linux.cs and Process.iOS.cs.
// NetWasm has no host processes. Each member keeps the association checks that upstream's Unix
// and Linux layers make (an unassociated Process still throws InvalidOperationException), then
// throws PlatformNotSupportedException where upstream would query or control the operating system.
// Where Unix answers with a constant or ignores the call (EnterDebugMode, LeaveDebugMode,
// PriorityBoostEnabled, the main-window members, Responding and WaitForInputIdle), NetWasm throws
// PlatformNotSupportedException instead, as API_POLICY.json classifies those members.

using System.ComponentModel;
using System.Runtime.Versioning;
using System.Security;
using Microsoft.Win32.SafeHandles;

namespace System.Diagnostics
{
    public partial class Process : IDisposable
    {
        public static void EnterDebugMode()
        {
            throw new PlatformNotSupportedException(SR.ProcessHostNotSupported);
        }

        public static void LeaveDebugMode()
        {
            throw new PlatformNotSupportedException(SR.ProcessHostNotSupported);
        }

        [CLSCompliant(false)]
        [SupportedOSPlatform("windows")]
        public static Process Start(string fileName, string userName, SecureString password, string domain)
        {
            throw new PlatformNotSupportedException(SR.ProcessStartWithPasswordAndDomainNotSupported);
        }

        [CLSCompliant(false)]
        [SupportedOSPlatform("windows")]
        public static Process Start(string fileName, string arguments, string userName, SecureString password, string domain)
        {
            throw new PlatformNotSupportedException(SR.ProcessStartWithPasswordAndDomainNotSupported);
        }

        /// <summary>Terminates the associated process immediately.</summary>
        [UnsupportedOSPlatform("ios")]
        [UnsupportedOSPlatform("tvos")]
        [SupportedOSPlatform("maccatalyst")]
        public void Kill()
        {
            // Upstream rejects the platform before the association check where processes cannot be started or killed.
            throw new PlatformNotSupportedException(SR.ProcessHostNotSupported);
        }

        /// <summary>Gets the main module for the associated process.</summary>
        public ProcessModule? MainModule
        {
            get
            {
                ProcessModuleCollection pmc = Modules;
                return pmc.Count > 0 ? pmc[0] : null;
            }
        }

        public IntPtr MainWindowHandle => throw new PlatformNotSupportedException(SR.ProcessHostNotSupported);

        public string MainWindowTitle => throw new PlatformNotSupportedException(SR.ProcessHostNotSupported);

        public bool Responding => throw new PlatformNotSupportedException(SR.ProcessHostNotSupported);

        private static bool CloseMainWindowCore() => throw new PlatformNotSupportedException(SR.ProcessHostNotSupported);

        private static bool WaitForInputIdleCore(int _ /*milliseconds*/) => throw new PlatformNotSupportedException(SR.ProcessHostNotSupported);

        public partial TimeSpan PrivilegedProcessorTime
        {
            get
            {
                EnsureState(State.HaveNonExitedId);
                throw new PlatformNotSupportedException(SR.ProcessHostNotSupported);
            }
        }

        public partial TimeSpan TotalProcessorTime
        {
            get
            {
                EnsureState(State.HaveNonExitedId);
                throw new PlatformNotSupportedException(SR.ProcessHostNotSupported);
            }
        }

        public partial TimeSpan UserProcessorTime
        {
            get
            {
                EnsureState(State.HaveNonExitedId);
                throw new PlatformNotSupportedException(SR.ProcessHostNotSupported);
            }
        }

        private DateTime StartTimeCore
        {
            get
            {
                EnsureState(State.HaveNonExitedId);
                throw new PlatformNotSupportedException(SR.ProcessHostNotSupported);
            }
        }

        private DateTime ExitTimeCore
        {
            get
            {
                EnsureState(State.HaveId);
                throw new PlatformNotSupportedException(SR.ProcessHostNotSupported);
            }
        }

        private static bool PriorityBoostEnabledCore
        {
            get { throw new PlatformNotSupportedException(SR.ProcessHostNotSupported); }
            set { throw new PlatformNotSupportedException(SR.ProcessHostNotSupported); }
        }

        private ProcessPriorityClass PriorityClassCore
        {
            get
            {
                EnsureState(State.HaveNonExitedId);
                throw new PlatformNotSupportedException(SR.ProcessHostNotSupported);
            }
            set
            {
                EnsureState(State.HaveNonExitedId);
                throw new PlatformNotSupportedException(SR.ProcessHostNotSupported);
            }
        }

        private IntPtr ProcessorAffinityCore
        {
            get
            {
                EnsureState(State.HaveNonExitedId);
                throw new PlatformNotSupportedException(SR.ProcessHostNotSupported);
            }
            set
            {
                EnsureState(State.HaveNonExitedId);
                throw new PlatformNotSupportedException(SR.ProcessHostNotSupported);
            }
        }

        private void GetWorkingSetLimits(out IntPtr minWorkingSet, out IntPtr maxWorkingSet)
        {
            EnsureState(State.HaveNonExitedId);
            throw new PlatformNotSupportedException(SR.ProcessHostNotSupported);
        }

        private void SetWorkingSetLimitsCore(IntPtr? newMin, IntPtr? newMax, out IntPtr resultingMin, out IntPtr resultingMax)
        {
            EnsureState(State.HaveNonExitedId);
            throw new PlatformNotSupportedException(SR.ProcessHostNotSupported);
        }

        /// <summary>Checks whether the process has exited and updates state accordingly.</summary>
        private void UpdateHasExited()
        {
            EnsureState(State.HaveId);
            throw new PlatformNotSupportedException(SR.ProcessHostNotSupported);
        }

        /// <summary>
        /// Instructs the Process component to wait the specified number of milliseconds for the associated process to exit.
        /// </summary>
        private bool WaitForExitCore(int milliseconds)
        {
            EnsureState(State.HaveId);
            throw new PlatformNotSupportedException(SR.ProcessHostNotSupported);
        }

        /// <devdoc>
        ///     Make sure we are watching for a process exit.
        /// </devdoc>
        private void EnsureWatchingForExit()
        {
            Debug.Assert(Associated, "Process.EnsureWatchingForExit called with no associated process");
            throw new PlatformNotSupportedException(SR.ProcessHostNotSupported);
        }

        /// <summary>Discards any information about the associated process.</summary>
        partial void RefreshCore();

        /// <summary>Additional logic invoked when the Process is closed.</summary>
        private static void CloseCore()
        {
            // No host resources are held for a process.
        }

        /// <summary>
        /// Gets a short-term handle to the process, with the given access.  If a handle exists,
        /// then it is reused.  If the process has exited, it throws an exception.
        /// </summary>
        private SafeProcessHandle GetProcessHandle()
        {
            EnsureState(State.HaveNonExitedId | State.IsLocal);
            throw new PlatformNotSupportedException(SR.ProcessHostNotSupported);
        }
    }
}
