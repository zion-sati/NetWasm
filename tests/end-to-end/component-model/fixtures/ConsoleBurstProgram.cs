// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;

// Writes many lines, then returns at once.
public static class Program
{
    public static int Main()
    {
        for (var line = 0; line < 2000; line++)
        {
            Console.WriteLine("line " + line);
        }

        return 0;
    }
}
