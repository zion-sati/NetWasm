// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Runtime.CompilerServices;

// A module initializer that writes to the console, which waits on wasi:cli/stdout: it runs once,
// before Main, as on desktop .NET.
public static class Startup
{
    public static int Runs;

    [ModuleInitializer]
    public static void Initialize()
    {
        Runs++;
        Console.WriteLine("initializer");
    }
}

public static class Program
{
    public static int Main()
    {
        Console.WriteLine("main " + Startup.Runs);
        return 0;
    }
}
