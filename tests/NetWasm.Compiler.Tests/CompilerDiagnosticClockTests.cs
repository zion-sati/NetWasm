// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;

using NetWasm.Compiler.Diagnostics;

namespace NetWasm.Compiler.Tests;

public sealed class CompilerDiagnosticClockTests
{
    [Fact]
    public void ReadReturnsTheCurrentUtcTime()
    {
        var clock = Assert.IsAssignableFrom<ICompilerDiagnosticClock>(new CompilerDiagnosticClock());
        var before = DateTimeOffset.UtcNow;

        var value = clock.Read();

        var after = DateTimeOffset.UtcNow;
        Assert.InRange(value, before, after);
        Assert.Equal(TimeSpan.Zero, value.Offset);
    }
}
