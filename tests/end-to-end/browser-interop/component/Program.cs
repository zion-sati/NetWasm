// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Runtime.InteropServices.JavaScript;
using System.Threading.Tasks;

public static class Program
{
    public static Task Main()
    {
        Console.WriteLine(Called());
        return Task.CompletedTask;
    }

    [JSExport("calledSync")]
    public static int Called() => 42;

    // A component command has no JavaScript export session. This unused method
    // must not introduce a host capture dependency, even without optimization.
    [JSExport("unusedAsync")]
    public static Task<int> Unused() => Task.FromException<int>(new FormatException("unused"));
}
