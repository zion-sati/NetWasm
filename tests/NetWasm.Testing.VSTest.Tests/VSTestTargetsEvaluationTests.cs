// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: MIT

using System.Diagnostics;
using System.Text.Json;

namespace NetWasm.Testing.VSTest.Tests;

// dotnet test passes the run settings after its argument separator, and its loggers, as global properties. A
// NetWasm test project keeps its own run settings beside them, and Microsoft.Testing.Platform's TRX options
// (report-trx, report-trx-filename), which upstream TUnit takes there, select VSTest's TRX logger.
public sealed class VSTestTargetsEvaluationTests
{
    private const string NetWasmRunSettings =
        "RunConfiguration.TestAdapterLoadingStrategy=Explicit,DefaultRuntimeProviders,ExtensionsDirectory;" +
        "TestRunParameters.Parameter(name=\"NetWasm.TargetFramework\",value=\"net10.0-netwasm\")";

    [Fact]
    public async Task WithoutUserSettingsTheRunTakesNetWasmsAlone()
    {
        var (runSettings, logger) = await EvaluateAsync();

        Assert.Equal(NetWasmRunSettings, runSettings);
        Assert.Equal(string.Empty, logger);
    }

    [Fact]
    public async Task UserRunSettingsKeepNetWasmsBesideThem()
    {
        var (runSettings, logger) = await EvaluateAsync("-p:VSTestCLIRunSettings=\"RunConfiguration.X=1\"");

        Assert.Equal("RunConfiguration.X=1;" + NetWasmRunSettings, runSettings);
        Assert.Equal(string.Empty, logger);
    }

    [Fact]
    public async Task ReportTrxSelectsTheTrxLogger()
    {
        var (runSettings, logger) = await EvaluateAsync("-p:VSTestCLIRunSettings=\"--report-trx\"");

        Assert.Equal(NetWasmRunSettings, runSettings);
        Assert.Equal("trx", logger);
    }

    [Fact]
    public async Task ReportTrxFileNameNamesTheLogFileBesideTheUsersLoggers()
    {
        var (runSettings, logger) = await EvaluateAsync(
            "-p:VSTestCLIRunSettings=\"--report-trx;--report-trx-filename;result.trx;RunConfiguration.X=1\"",
            "-p:VSTestLogger=html");

        Assert.Equal("RunConfiguration.X=1;" + NetWasmRunSettings, runSettings);
        Assert.Equal("html;trx;LogFileName=result.trx", logger);
    }

    private static async Task<(string RunSettings, string Logger)> EvaluateAsync(params string[] properties)
    {
        var root = Path.Combine(Path.GetTempPath(), $"netwasm-vstest-targets-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var targets = Path.Combine(
                RepositoryRoot(), "src", "NetWasm.Testing.VSTest", "buildTransitive", "NetWasm.Testing.VSTest.targets");
            var project = Path.Combine(root, "Example.Tests.proj");
            await File.WriteAllTextAsync(project, $"""
                <Project>
                  <PropertyGroup>
                    <NetWasmGuestFramework>true</NetWasmGuestFramework>
                    <TargetFramework>net10.0-netwasm</TargetFramework>
                    <TargetFrameworkIdentifier>.NETCoreApp</TargetFrameworkIdentifier>
                  </PropertyGroup>
                  <Import Project="{targets}" />
                </Project>
                """, TestContext.Current.CancellationToken);
            var start = new ProcessStartInfo(NetWasm.TestInfrastructure.DotNetHost.Executable)
            {
                WorkingDirectory = root,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            foreach (var argument in (string[])["msbuild", project, "-nologo",
                "-getProperty:VSTestCLIRunSettings", "-getProperty:VSTestLogger", .. properties])
            {
                start.ArgumentList.Add(argument);
            }
            using var process = Process.Start(start)!;
            var output = await process.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken);
            var error = await process.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken);
            await process.WaitForExitAsync(TestContext.Current.CancellationToken);
            Assert.True(process.ExitCode == 0, output + error);
            using var document = JsonDocument.Parse(output);
            var values = document.RootElement.GetProperty("Properties");
            return (values.GetProperty("VSTestCLIRunSettings").GetString()!, values.GetProperty("VSTestLogger").GetString()!);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(
            directory.FullName, "src", "NetWasm.Testing.VSTest", "NetWasm.Testing.VSTest.csproj")))
        {
            directory = directory.Parent;
        }
        Assert.NotNull(directory);
        return directory!.FullName;
    }
}
