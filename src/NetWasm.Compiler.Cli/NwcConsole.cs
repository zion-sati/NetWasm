// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;

namespace NetWasm.Compiler.Cli;

// The text nwc writes to its standard output: the banner, help and every
// logged line, as csc writes them.
internal interface IConsole
{
    void WriteLine(string line);
}

internal sealed class SystemConsole : IConsole
{
    public void WriteLine(string line) => Console.Out.WriteLine(line);
}
