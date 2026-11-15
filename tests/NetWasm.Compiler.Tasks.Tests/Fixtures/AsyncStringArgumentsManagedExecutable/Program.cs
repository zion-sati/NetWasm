// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

namespace NetWasm.Compiler.Tasks.Tests.Fixtures.AsyncStringArgumentsManagedExecutable;

internal static class Program
{
    public static async Task<int> Main(string[] arguments)
    {
        await Task.Yield();
        return arguments.Length;
    }
}

public sealed class Marker;
