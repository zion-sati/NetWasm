// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// NetWasm platform layer for ProcessManager, following dotnet/runtime commit
// deae2e32bf0e018e09d64597b7c59b62b73bb3ce ProcessManager.Unix.cs and ProcessManager.iOS.cs.
// NetWasm has no host process table, so process lookup, enumeration and inspection throw
// PlatformNotSupportedException. Upstream's iOS layer answers GetModules with an empty collection;
// NetWasm throws rather than report a process with no modules. Remote machines stay unsupported
// as on Unix; without a host name, every machine name other than "." counts as remote.

using System.Collections.Generic;

namespace System.Diagnostics
{
    internal static partial class ProcessManager
    {
#pragma warning disable IDE0060
        /// <summary>Gets whether the process with the specified ID on the specified machine is currently running.</summary>
        public static bool IsProcessRunning(int processId, string machineName, bool isRemoteMachine) =>
            IsProcessRunning(processId);

        /// <summary>Gets whether the process with the specified ID is currently running.</summary>
        public static bool IsProcessRunning(int processId) =>
            throw new PlatformNotSupportedException(SR.ProcessHostNotSupported);

        /// <summary>Gets the ProcessInfo for the specified process ID on the specified machine.</summary>
        public static ProcessInfo? GetProcessInfo(int processId, string machineName, bool isRemoteMachine) =>
            throw new PlatformNotSupportedException(SR.ProcessHostNotSupported);

        internal static bool HandleRemoteMachineSupport(string machineName)
        {
            ArgumentException.ThrowIfNullOrEmpty(machineName);
            if (machineName != ".")
            {
                throw new PlatformNotSupportedException(SR.RemoteMachinesNotSupported);
            }
            return false;
        }

        /// <summary>Gets process infos for each process on the specified machine.</summary>
        public static void GetProcessInfos(ref ArrayBuilder<ProcessInfo> builder, string? processNameFilter, string machineName, bool isRemoteMachine) =>
            GetProcessInfos(ref builder, processNameFilter);

        public static void GetProcessInfos(ref ArrayBuilder<ProcessInfo> builder, string? processNameFilter) =>
            throw new PlatformNotSupportedException(SR.ProcessHostNotSupported);

        internal static ProcessModuleCollection GetModules(int processId) =>
            throw new PlatformNotSupportedException(SR.ProcessHostNotSupported);

        internal static string? GetProcessName(int processId, string machineName, bool isRemoteMachine, ref ProcessInfo? processInfo) =>
            throw new PlatformNotSupportedException(SR.ProcessHostNotSupported);
#pragma warning restore IDE0060
    }
}
