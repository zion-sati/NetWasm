// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

using static CompilerTestSupport;

// A broad attribute query (a filter that takes attribute types the program does not name) that selects an attribute
// the compiler cannot build keeps its run-time path, which throws PlatformNotSupportedException when reached, so the
// build warns NW1019, naming the method, the query and why the attribute cannot be built. Here the program was built
// against a library whose attribute has a Weight property and runs with one that lacks it, so the attribute's named
// argument has nothing to set. A query on a Type known only at run time that would build that attribute for one of the
// program's run-time types warns too. A broad query whose attributes the compiler can build answers without a warning,
// and the warnings hold where inlining moves a query into its caller (the warning then names the caller) and where a
// compile restores compiled units.
public sealed class UnplannedAttributeQueryCompilationTests
{
    [Theory]
    [InlineData(FinalWasmOptimization.None)]
    [InlineData(FinalWasmOptimization.O1)]
    [InlineData(FinalWasmOptimization.O3)]
    [InlineData(FinalWasmOptimization.Oz)]
    public void ABroadQueryOfAnAttributeTheCompilerCannotBuildWarnsAndThrowsWhenReached(
        FinalWasmOptimization optimization)
    {
        using var assets = TestAssets.Create();
        var logs = new CapturingLoggerProvider();

        var result = Compile(Options(assets, optimization), logs);

        var warnings = logs.Entries.Where(entry => entry.Level == LogLevel.Warning).ToArray();
        Assert.Equal(2, warnings.Length);
        Assert.All(warnings, warning =>
        {
            Assert.Equal("NW1019", warning.Id.Name);
            Assert.Equal((int)DiagnosticCode.UnplannedAttributeQuery, warning.Id.Id);
            Assert.Contains("'Weight' has no writable member", warning.Message, StringComparison.Ordinal);
            Assert.Contains("the query throws PlatformNotSupportedException when reached", warning.Message,
                StringComparison.Ordinal);
            Assert.DoesNotContain("CountPlain", warning.Message, StringComparison.Ordinal);
        });
        var broad = Assert.Single(warnings, warning => warning.Message.Contains("with filter System.Attribute",
            StringComparison.Ordinal));
        Assert.Matches(@"^UnplannedAttributeApplication\.EntryPoint::(CountTarget|Run) calls " +
            @"System\.Reflection\.MemberInfo::GetCustomAttributes on UnplannedAttributeApplication\.Target with filter " +
            @"System\.Attribute, which NetWasm cannot answer at compile time \(it may select an attribute NetWasm cannot " +
            @"build: custom attribute named argument 'Weight' has no writable member\)", broad.Message);
        var runTime = Assert.Single(warnings, warning => warning.Message.Contains("on a Type known only at run time",
            StringComparison.Ordinal));
        Assert.StartsWith("UnplannedAttributeApplication.EntryPoint::WeightOf calls " +
            "System.Reflection.CustomAttributeExtensions::GetCustomAttribute on a Type known only at run time with filter " +
            "UnplannedAttributeLibrary.WeightedAttribute, which NetWasm cannot answer at compile time (an attribute it " +
            "selects on a type the program has at run time cannot be built", runTime.Message, StringComparison.Ordinal);
        // CountPlain answers 1 (its buildable attribute); CountTarget's and WeightOf's queries throw where the warnings
        // say they do.
        Assert.Equal(1 + 100 + 1000, ExecuteWithStandardWasiNode(result.ApplicationModule, assets.Directory, 3,
            WasmTarget.Wasm32, result.StaticDataEnd));
    }

    // A compile that restores the method's compiled unit still analyzes its body, so it warns as a clean compile does.
    [Theory]
    [InlineData(FinalWasmOptimization.None)]
    [InlineData(FinalWasmOptimization.Oz)]
    public void ACompileThatRestoresTheUnitStillWarns(FinalWasmOptimization optimization)
    {
        using var assets = TestAssets.Create();
        var options = Options(assets, optimization) with
        {
            IntermediateOutputPath = Path.Combine(assets.Directory, "obj"),
            CompiledUnits = true,
        };
        Compile(options, new CapturingLoggerProvider());
        var logs = new CapturingLoggerProvider();

        Compile(options, logs);

        var summary = File.ReadAllText(Directory.EnumerateFiles(Path.Combine(assets.Directory, "obj"), "last-compile.txt",
            SearchOption.AllDirectories).Single());
        var restored = int.Parse(System.Text.RegularExpressions.Regex.Match(summary, @"restored (\d+)").Groups[1].Value,
            System.Globalization.CultureInfo.InvariantCulture);
        Assert.True(restored > 0, summary);
        var warnings = logs.Entries.Where(entry => entry.Level == LogLevel.Warning).ToArray();
        Assert.Equal(2, warnings.Length);
        Assert.All(warnings, warning => Assert.Equal("NW1019", warning.Id.Name));
        Assert.Contains(warnings, warning => warning.Message.StartsWith("UnplannedAttributeApplication.EntryPoint::CountTarget",
            StringComparison.Ordinal));
        Assert.Contains(warnings, warning => warning.Message.StartsWith("UnplannedAttributeApplication.EntryPoint::WeightOf",
            StringComparison.Ordinal));
    }

    private static CompilerOptions Options(TestAssets assets, FinalWasmOptimization optimization)
    {
        var library = assets.CompileSource("UnplannedAttributeLibrary", """
            namespace UnplannedAttributeLibrary;
            [System.AttributeUsage(System.AttributeTargets.Class)]
            public sealed class WeightedAttribute : System.Attribute
            {
                public int Weight { get; set; }
            }
            [System.AttributeUsage(System.AttributeTargets.Class)]
            public sealed class KeptAttribute : System.Attribute
            {
            }
            """);
        var application = assets.CompileSource("UnplannedAttributeApplication", """
            using System;
            using System.Reflection;
            using System.Runtime.CompilerServices;
            namespace UnplannedAttributeApplication;

            [UnplannedAttributeLibrary.Weighted(Weight = 3)]
            public sealed class Target
            {
            }

            [UnplannedAttributeLibrary.Kept]
            public sealed class Plain
            {
            }

            public static class EntryPoint
            {
                // Small enough that the optimizing levels inline it into Run.
                private static int CountTarget() => typeof(Target).GetCustomAttributes(typeof(Attribute), false).Length;

                [MethodImpl(MethodImplOptions.NoInlining)]
                private static int CountPlain() => typeof(Plain).GetCustomAttributes(typeof(Attribute), false).Length;

                [MethodImpl(MethodImplOptions.NoInlining)]
                private static Type Pick(int input) => input > 0 ? typeof(Target) : typeof(Plain);

                // The run-time Type query would build Target's attribute.
                [MethodImpl(MethodImplOptions.NoInlining)]
                private static int WeightOf(int input) =>
                    Pick(input).GetCustomAttribute<UnplannedAttributeLibrary.WeightedAttribute>() is null ? 0 : 1;

                public static int Run(int input)
                {
                    var result = CountPlain();
                    try { result += CountTarget(); }
                    catch (PlatformNotSupportedException error) { result += error.Message.Length > 0 ? 100 : 50; }
                    try { result += WeightOf(input); }
                    catch (PlatformNotSupportedException error) { result += error.Message.Length > 0 ? 1000 : 500; }
                    return result;
                }
            }
            """, library);
        // The library the program runs with: the attribute has no Weight to set.
        _ = assets.CompileSource("UnplannedAttributeLibrary", """
            namespace UnplannedAttributeLibrary;
            [System.AttributeUsage(System.AttributeTargets.Class)]
            public sealed class WeightedAttribute : System.Attribute
            {
            }
            [System.AttributeUsage(System.AttributeTargets.Class)]
            public sealed class KeptAttribute : System.Attribute
            {
            }
            """);
        return new(application, [assets.CoreLib, library], "UnplannedAttributeApplication.EntryPoint", "Run", [],
            Optimization: optimization);
    }

    private static CompilationResult Compile(CompilerOptions options, CapturingLoggerProvider logs)
    {
        options = options with
        {
            ReferencePaths = [.. TestAssets.WithFrameworkReferences(options.EntryAssemblyPath, options.ReferencePaths)],
        };
        using var services = new ServiceCollection()
            .AddNetWasmCompiler()
            .AddSingleton<ILoggerProvider>(logs)
            .BuildServiceProvider();
        return services.GetRequiredService<INetWasmCompiler>().Compile(options);
    }

    private sealed class CapturingLoggerProvider : ILoggerProvider, ILogger
    {
        public ConcurrentQueue<(LogLevel Level, EventId Id, string Message)> Entries { get; } = new();

        public ILogger CreateLogger(string categoryName) => this;
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Warning;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (IsEnabled(logLevel))
                Entries.Enqueue((logLevel, eventId, formatter(state, exception)));
        }
        public void Dispose() { }
    }
}
