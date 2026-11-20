// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

namespace NetWasm.Compiler.ComponentModel;

public sealed record ToolResult(
    int ExitCode,
    string StandardOutput,
    string StandardError)
{
    // What a failed tool reported, on one line: its error output, or, when it wrote none, how it ended, so a crash or
    // an out-of-memory kill is named. .NET reports a Unix process that a signal ended as 128 plus the signal.
    public string FailureDescription
    {
        get
        {
            var error = StandardError.Replace('\r', ' ').Replace('\n', ' ').Trim();
            return error.Length != 0 ? error : ExitCode switch
            {
                134 => "it aborted (signal 6) without an error message",
                137 => "it was killed (signal 9), most often because the system ran out of memory",
                139 => "it crashed (signal 11), as a stack overflow does, without an error message",
                > 128 and < 160 => $"it ended on signal {ExitCode - 128} without an error message",
                _ => $"it exited with code {ExitCode} without an error message",
            };
        }
    }
}
