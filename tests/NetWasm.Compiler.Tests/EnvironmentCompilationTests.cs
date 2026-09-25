using System.Collections.Immutable;
using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

public sealed class EnvironmentCompilationTests
{
    [Theory]
    [InlineData(false, WasmTarget.Wasm32)]
    [InlineData(true, WasmTarget.Wasm32)]
    [InlineData(false, WasmTarget.Wasm64)]
    [InlineData(true, WasmTarget.Wasm64)]
    public void EnumerationAndMutationShareGuestState(bool optimize, WasmTarget target)
    {
        using var assets = TestAssets.Create();
        ICompilationScenarioExecutor executor = new CompilationScenarioExecutor(assets);
        var result = executor.Execute(new CompilationScenario(
            $"EnvironmentMutation{optimize}{target}",
            """
            using System;

            namespace EnvironmentFixture;

            public static class EntryPoint
            {
                public static int Run(int input)
                {
                    if (Environment.GetEnvironmentVariable("A\0ignored") != "one")
                        return -1;
                    var first = Environment.GetEnvironmentVariables();
                    if (first.Count != 3 || (string)first["A"] != "one")
                        return -2;
                    first["A"] = "copy only";
                    first["INJECTED"] = "copy only";
                    if (Environment.GetEnvironmentVariable("A") != "one"
                        || Environment.GetEnvironmentVariable("INJECTED") != null)
                        return -3;
                    Environment.SetEnvironmentVariable("A", "changed");
                    Environment.SetEnvironmentVariable("NEW", "café水", EnvironmentVariableTarget.Process);
                    Environment.SetEnvironmentVariable("EMPTY", "");
                    Environment.SetEnvironmentVariable("TRIM\0ignored", "value\0ignored");
                    if (Environment.GetEnvironmentVariable("A", EnvironmentVariableTarget.Process) != "changed"
                        || Environment.GetEnvironmentVariable("TRIM") != "value"
                        || Environment.GetEnvironmentVariable("EMPTY") != ""
                        || Environment.ExpandEnvironmentVariables("prefix%NEW%suffix") != "prefixcafé水suffix")
                        return -4;
                    var second = Environment.GetEnvironmentVariables(EnvironmentVariableTarget.Process);
                    if ((string)second["NEW"] != "café水" || second.Contains("INJECTED"))
                        return -5;
                    Environment.SetEnvironmentVariable("NEW", null);
                    Environment.SetEnvironmentVariable("NEW", null);
                    if (Environment.GetEnvironmentVariable("NEW") != null || (string)second["NEW"] != "café水")
                        return -6;
                    var rejected = 0;
                    try { Environment.SetEnvironmentVariable(null, "x"); }
                    catch (ArgumentNullException) { rejected++; }
                    try { Environment.SetEnvironmentVariable("", "x"); }
                    catch (ArgumentException) { rejected++; }
                    try { Environment.SetEnvironmentVariable("\0name", "x"); }
                    catch (ArgumentException) { rejected++; }
                    try { Environment.SetEnvironmentVariable("A=B", "x"); }
                    catch (ArgumentException) { rejected++; }
                    foreach (var scope in new[] { EnvironmentVariableTarget.User, EnvironmentVariableTarget.Machine })
                    {
                        try { Environment.GetEnvironmentVariable("A", scope); }
                        catch (PlatformNotSupportedException) { rejected++; }
                        try { Environment.GetEnvironmentVariables(scope); }
                        catch (PlatformNotSupportedException) { rejected++; }
                        try { Environment.SetEnvironmentVariable("A", "wrong", scope); }
                        catch (PlatformNotSupportedException) { rejected++; }
                    }
                    try { Environment.GetEnvironmentVariable("A", (EnvironmentVariableTarget)99); }
                    catch (ArgumentOutOfRangeException) { rejected++; }
                    try { Environment.GetEnvironmentVariables((EnvironmentVariableTarget)99); }
                    catch (ArgumentOutOfRangeException) { rejected++; }
                    try { Environment.SetEnvironmentVariable("A", "wrong", (EnvironmentVariableTarget)99); }
                    catch (ArgumentOutOfRangeException) { rejected++; }
                    if (rejected != 13 || Environment.GetEnvironmentVariable("A") != "changed")
                        return -7;
                    return input + 1;
                }
            }
            """,
            "EnvironmentFixture.EntryPoint",
            optimize,
            target,
            41,
            [])
        {
            Exports = [new RequestedExport("run", "EnvironmentFixture.EntryPoint", "Run")],
            WitPath = Path.Combine(assets.Root, "wit", "netwasm-platform-1.0.0"),
            WitWorld = "netwasm:platform@1.0.0/platform",
            ExpectedEnvironmentReads = 1,
            ExpectedPreopenReads = 0,
            Environment = ImmutableDictionary<string, string>.Empty
                .Add("A", "one")
                .Add("EMPTY", "")
                .Add("CONFIG", "startup"),
        });

        Assert.Equal(42, result);
    }

    [Theory]
    [InlineData(false, WasmTarget.Wasm32)]
    [InlineData(true, WasmTarget.Wasm32)]
    [InlineData(false, WasmTarget.Wasm64)]
    [InlineData(true, WasmTarget.Wasm64)]
    public void GuestPlatformFactsFollowTargetAndSingleThreadedProfile(bool optimize, WasmTarget target)
    {
        using var assets = TestAssets.Create();
        ICompilationScenarioExecutor executor = new CompilationScenarioExecutor(assets);
        var result = executor.Execute(new CompilationScenario(
            $"EnvironmentFacts{optimize}{target}",
            """
            using System;

            namespace EnvironmentFixture;

            public static class EntryPoint
            {
                public static int Run(int width)
                {
                    if (Environment.Is64BitProcess != (width == 64))
                        return -1;
                    if (Environment.ProcessorCount != 1
                        || Environment.CurrentManagedThreadId != 1
                        || Environment.HasShutdownStarted)
                        return -2;
                    if (Environment.NewLine.Length != 1 || Environment.NewLine[0] != '\n')
                        return -3;
                    if (Rejects(() => { _ = Environment.MachineName; })
                        + Rejects(() => { _ = Environment.UserName; })
                        + Rejects(() => { _ = Environment.UserDomainName; })
                        + Rejects(() => { _ = Environment.ProcessId; })
                        + Rejects(() => { _ = Environment.Is64BitOperatingSystem; }) != 5)
                        return -4;
                    return 42;
                }

                private static int Rejects(Action read)
                {
                    try
                    {
                        read();
                        return 0;
                    }
                    catch (PlatformNotSupportedException)
                    {
                        return 1;
                    }
                }
            }
            """,
            "EnvironmentFixture.EntryPoint",
            optimize,
            target,
            target == WasmTarget.Wasm64 ? 64 : 32,
            [])
        {
            Exports = [new RequestedExport("run", "EnvironmentFixture.EntryPoint", "Run")],
            WitPath = Path.Combine(assets.Root, "wit", "netwasm-platform-1.0.0"),
            WitWorld = "netwasm:platform@1.0.0/platform",
            ExpectedEnvironmentReads = 0,
            ExpectedPreopenReads = 0,
        });

        Assert.Equal(42, result);
    }

    [Theory]
    [InlineData(false, WasmTarget.Wasm32)]
    [InlineData(true, WasmTarget.Wasm32)]
    [InlineData(false, WasmTarget.Wasm64)]
    [InlineData(true, WasmTarget.Wasm64)]
    public void PublicEnvironmentLookupAndExpansionRespectGuestValues(bool optimize, WasmTarget target)
    {
        using var assets = TestAssets.Create();
        ICompilationScenarioExecutor executor = new CompilationScenarioExecutor(assets);
        var result = executor.Execute(new CompilationScenario(
            $"EnvironmentLookup{optimize}{target}",
            """
            using System;

            namespace EnvironmentFixture;

            public static class EntryPoint
            {
                public static int Run(int input)
                {
                    if (Environment.GetEnvironmentVariable("APPLICATION_MODE") != "test"
                        || Environment.GetEnvironmentVariable("LANGUAGE") != "café水"
                        || Environment.GetEnvironmentVariable("EMPTY") != ""
                        || Environment.GetEnvironmentVariable("MISSING") != null
                        || Environment.GetEnvironmentVariable("application_mode") != null
                        || Environment.GetEnvironmentVariable("") != null
                        || Environment.GetEnvironmentVariable("A=B") != null)
                        return -1;

                    // Repeated calls must preserve values and exercise the cached reader.
                    if (Environment.GetEnvironmentVariable("LANGUAGE") != "café水")
                        return -2;

                    if (Environment.ExpandEnvironmentVariables("") != ""
                        || Environment.ExpandEnvironmentVariables("literal") != "literal"
                        || Environment.ExpandEnvironmentVariables("%") != "%"
                        || Environment.ExpandEnvironmentVariables("%%") != "%%"
                        || Environment.ExpandEnvironmentVariables("%%%") != "%%%"
                        || Environment.ExpandEnvironmentVariables("%APPLICATION_MODE%") != "test"
                        || Environment.ExpandEnvironmentVariables("%LANGUAGE%") != "café水"
                        || Environment.ExpandEnvironmentVariables("%EMPTY%") != ""
                        || Environment.ExpandEnvironmentVariables("%MISSING%") != "%MISSING%"
                        || Environment.ExpandEnvironmentVariables("%NESTED%") != "%APPLICATION_MODE%")
                        return -4;

                    try
                    {
                        Environment.ExpandEnvironmentVariables(null);
                        return -5;
                    }
                    catch (ArgumentNullException)
                    {
                    }

                    try
                    {
                        Environment.GetEnvironmentVariable(null);
                        return -3;
                    }
                    catch (ArgumentNullException)
                    {
                        return input + 1;
                    }
                }
            }
            """,
            "EnvironmentFixture.EntryPoint",
            optimize,
            target,
            41,
            [])
        {
            Exports = [new RequestedExport("run", "EnvironmentFixture.EntryPoint", "Run")],
            WitPath = Path.Combine(assets.Root, "wit", "netwasm-platform-1.0.0"),
            WitWorld = "netwasm:platform@1.0.0/platform",
            ExpectedEnvironmentReads = 1,
            ExpectedPreopenReads = 0,
            Environment = ImmutableDictionary<string, string>.Empty
                .Add("APPLICATION_MODE", "test")
                .Add("LANGUAGE", "café水")
                .Add("NESTED", "%APPLICATION_MODE%")
                .Add("EMPTY", ""),
        });

        Assert.Equal(42, result);
    }
}
