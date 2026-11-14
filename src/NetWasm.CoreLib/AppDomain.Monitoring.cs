// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Monitoring accessors follow dotnet/runtime; unavailable host metrics retain PNSE.
namespace System;

public sealed partial class AppDomain
{
    public static bool MonitoringIsEnabled
    {
        get => true;
        set
        {
            if (!value)
                throw new ArgumentException("Argument must be true.");
        }
    }

    public long MonitoringSurvivedMemorySize => MonitoringSurvivedProcessMemorySize;

    public static long MonitoringSurvivedProcessMemorySize
    {
        get
        {
            var memory = GC.GetGCMemoryInfo();
            return memory.HeapSizeBytes - memory.FragmentedBytes;
        }
    }

    public long MonitoringTotalAllocatedMemorySize => GC.GetTotalAllocatedBytes(precise: false);

    // As upstream's WASI build reports it: WASI has no processor-time clock, so SystemNative_GetCpuUtilization leaves
    // the user time at zero.
    public TimeSpan MonitoringTotalProcessorTime => TimeSpan.Zero;
}
