// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.ComponentModel;

namespace NetWasm.Compiler.ComponentModel.Tests;

// A failed tool's message: its error output, or how it ended when it wrote none, so a crash or a kill is named.
public sealed class ToolResultTests
{
    [Theory]
    [InlineData(1, "bad\r\nmodule\n", "bad  module")]
    [InlineData(1, "  ", "it exited with code 1 without an error message")]
    [InlineData(134, "", "it aborted (signal 6) without an error message")]
    [InlineData(137, "", "it was killed (signal 9), most often because the system ran out of memory")]
    [InlineData(139, "", "it crashed (signal 11), as a stack overflow does, without an error message")]
    [InlineData(143, "", "it ended on signal 15 without an error message")]
    [InlineData(-1073741571, "", "it exited with code -1073741571 without an error message")]
    public void DescribesAFailureByItsErrorOrHowTheToolEnded(int exitCode, string error, string expected) =>
        Assert.Equal(expected, new ToolResult(exitCode, "output", error).FailureDescription);
}
