// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Hosting.Build.JavaScript;

namespace NetWasm.Hosting.Build.Tests;

public sealed class JavaScriptProcessRunnerTests
{
    // A failing tool's own diagnostic reaches the build error, as the bundler's
    // names an import it could not resolve (issue #141). The .NET host stands
    // in for Node: asked to run a missing file, it fails and says why.
    [Fact]
    public void FailureCarriesTheToolsDiagnostic()
    {
        using var directory = TemporaryDirectory.Create();
        var host = System.Environment.GetEnvironmentVariable("DOTNET_HOST_PATH");
        Assert.False(string.IsNullOrEmpty(host), "dotnet test sets DOTNET_HOST_PATH.");

        var request = new JavaScriptProcessRequest(host!, Path.Combine(directory.Path, "missing-tool.dll"), [], "test tool");

        var failure = Assert.Throws<InvalidOperationException>(() => new JavaScriptProcessRunner().Run(request));

        const string Prefix = "The NetWasm test tool failed: ";
        Assert.StartsWith(Prefix, failure.Message, StringComparison.Ordinal);
        Assert.True(failure.Message.Length > Prefix.Length + 1, failure.Message);
    }
}
