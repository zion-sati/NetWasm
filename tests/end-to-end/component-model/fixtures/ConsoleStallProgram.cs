// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;

// Writes a line, then computes without ever waiting again.
public static class Program
{
    public static int Main()
    {
        Console.WriteLine("stalled");
        var spins = 0L;
        while (true)
        {
            spins++;
        }
    }
}
