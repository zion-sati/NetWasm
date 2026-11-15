// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Hosting.Build.JavaScript;

namespace NetWasm.Hosting.Build.Tests;

public sealed class JavaScriptParameterNamesTests
{
    [Fact]
    public void KeepsUsableSourceNamesAndFallsBackPositionally()
    {
        Assert.Equal(
            ["total", "argument1", "argument2", "argument3", "argument4", "ünïcode", "argument6"],
            JavaScriptParameterNames.Resolve(
                ["total", "class", "1st", "", "total", "ünïcode", "options"], 7, ["options"]).ToArray());
        // Missing names fall back, and a fallback taken by a source name gains a prefix.
        Assert.Equal(["argument1", "_argument1", "argument2"],
            JavaScriptParameterNames.Resolve(["argument1", "bad name"], 3, []).ToArray());
    }

    [Fact]
    public void ConvertsKebabCaseWitNames()
    {
        Assert.Equal("startIndex", JavaScriptParameterNames.FromWit("start-index"));
        Assert.Equal("aB", JavaScriptParameterNames.FromWit("a--b"));
        Assert.Equal("value", JavaScriptParameterNames.FromWit("value"));
        Assert.Equal("", JavaScriptParameterNames.FromWit(""));
    }
}
