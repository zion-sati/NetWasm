// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Runtime.InteropServices.JavaScript;
using System.Threading.Tasks;

// Issue #89 repro 2: a JSSubscription callback, which reaches the finalization runtime feature.
public static class Program
{
    public static async Task<int> Main()
    {
        using var subscription = Host.Register(value => value * 2);
        await new TaskCompletionSource().Task;
        return 0;
    }
}

internal static class Host
{
    [JSImport("register", "repro.host")]
    internal static extern JSSubscription Register(Func<int, int> callback);
}
