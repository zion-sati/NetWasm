// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// NetWasm platform layer for ProcessThread, following dotnet/runtime commit
// deae2e32bf0e018e09d64597b7c59b62b73bb3ce ProcessThread.Unix.cs and ProcessThread.iOS.cs.
// Thread priority, affinity, ideal-processor and timing operations need the host thread, so they
// throw PlatformNotSupportedException. Unix ignores IdealProcessor, ResetIdealProcessor and
// PriorityBoostEnabled; NetWasm throws instead, as API_POLICY.json classifies those members.

using System.Runtime.Versioning;

namespace System.Diagnostics
{
    public partial class ProcessThread
    {
        public int IdealProcessor
        {
            set { throw new PlatformNotSupportedException(SR.ProcessHostNotSupported); }
        }

        public void ResetIdealProcessor()
        {
            throw new PlatformNotSupportedException(SR.ProcessHostNotSupported);
        }

        private static bool PriorityBoostEnabledCore
        {
            get { throw new PlatformNotSupportedException(SR.ProcessHostNotSupported); }
            set { throw new PlatformNotSupportedException(SR.ProcessHostNotSupported); }
        }

        [SupportedOSPlatform("windows")]
        public IntPtr ProcessorAffinity
        {
            set { throw new PlatformNotSupportedException(); } // No ability to change the affinity of a thread in an arbitrary process
        }

        private static ThreadPriorityLevel PriorityLevelCore
        {
            get { throw new PlatformNotSupportedException(SR.ProcessHostNotSupported); }
            set { throw new PlatformNotSupportedException(SR.ProcessHostNotSupported); }
        }

        public TimeSpan PrivilegedProcessorTime
        {
            get { throw new PlatformNotSupportedException(SR.ProcessHostNotSupported); }
        }

        private static DateTime GetStartTime() => throw new PlatformNotSupportedException(SR.ProcessHostNotSupported);

        public TimeSpan TotalProcessorTime
        {
            get { throw new PlatformNotSupportedException(SR.ProcessHostNotSupported); }
        }

        public TimeSpan UserProcessorTime
        {
            get { throw new PlatformNotSupportedException(SR.ProcessHostNotSupported); }
        }
    }
}
