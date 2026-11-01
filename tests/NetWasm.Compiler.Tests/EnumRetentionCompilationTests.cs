using System.Globalization;
using System.Text;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Diagnostics;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

public sealed class EnumRetentionCompilationTests(ITestOutputHelper output)
{
    [Fact]
    public void LibraryCacheDoesNotFreezeEntryAssemblyEnumDemand()
    {
        using var assets = TestAssets.Create();
        var intermediate = Path.Combine(assets.Directory, "obj");
        var library = assets.CompileOptimizedSource("EnumCacheLibrary", """
            public enum Named : ulong { One = 1, Two = 2 }
            public static class Library
            {
                public static T Identity<T>(T value) => value;
            }
            """);
        var numericSource = Source("Numeric(input)");
        var namesSource = Source("Names(input)");

        var assembly = assets.CompileOptimizedSource(
            "EnumCacheApplication", numericSource, library);
        var firstObserver = new RecordingObserver();
        var first = NetWasmCompiler.Compile(Options(assembly, firstObserver));
        Assert.True(first.ApplicationModule.AsSpan().IndexOf(
            Encoding.Unicode.GetBytes("One")) < 0);
        Assert.Equal(1, CompilerTestSupport.ExecuteWithStandardWasiNode(
            first.ApplicationModule, assets.Directory, 1,
            WasmTarget.Wasm32, first.StaticDataEnd));

        assembly = assets.CompileOptimizedSource(
            "EnumCacheApplication", namesSource, library);
        var secondObserver = new RecordingObserver();
        var second = NetWasmCompiler.Compile(Options(assembly, secondObserver));

        Assert.True(second.ApplicationModule.AsSpan().IndexOf(
            Encoding.Unicode.GetBytes("One")) >= 0);
        Assert.Equal(1, CompilerTestSupport.ExecuteWithStandardWasiNode(
            second.ApplicationModule, assets.Directory, 1,
            WasmTarget.Wasm32, second.StaticDataEnd));
        var cache = Assert.Single(secondObserver.Reports).FrontendCache;
        Assert.NotNull(cache);
        Assert.True(cache.Hits > 0);

        var cold = NetWasmCompiler.Compile(Options(
            assembly, new RecordingObserver(), enableCache: false));
        Assert.Equal(cold.ApplicationModule, second.ApplicationModule);

        assembly = assets.CompileOptimizedSource(
            "EnumCacheApplication", numericSource, library);
        var thirdObserver = new RecordingObserver();
        var third = NetWasmCompiler.Compile(Options(assembly, thirdObserver));
        Assert.Equal(first.ApplicationModule, third.ApplicationModule);
        Assert.True(third.ApplicationModule.AsSpan().IndexOf(
            Encoding.Unicode.GetBytes("One")) < 0);
        Assert.NotNull(Assert.Single(thirdObserver.Reports).FrontendCache);

        static string Source(string call) => $$"""
            using System;
            public static class EntryPoint
            {
                public static int Run(int input) => {{call}};

                private static int Numeric(int input)
                {
                    var value = Library.Identity((Named)(ulong)input);
                    return ((Enum)value).GetHashCode();
                }

                private static int Names(int input)
                {
                    var value = Library.Identity((Named)(ulong)input);
                    return Enum.GetName(value) == "One" ? 1 : 0;
                }
            }
            """;

        CompilerOptions Options(
            string entryAssembly,
            ICompilerMetricsObserver observer,
            bool enableCache = true) => new(
            entryAssembly,
            [assets.CoreLib, library],
            "EntryPoint",
            "Run",
            [],
            MetricsObserver: observer,
            EnableFrontendCache: enableCache,
            IntermediateOutputPath: intermediate);
    }

    [Theory]
    [InlineData("Library.Names<Named>().Length")]
    [InlineData("Library.Values<Named>().Length")]
    [InlineData("Enum.IsDefined((Named)(ulong)input) ? 1 : 0")]
    [InlineData("Enum.GetName((Named)(ulong)input) != null ? 1 : 0")]
    [InlineData("((Enum)(Named)(ulong)input).GetHashCode()")]
    public void ClosedLibraryEnumOperationsUseUnderlyingStorage(string expression)
    {
        using var assets = TestAssets.Create();
        var library = assets.CompileOptimizedSource("EnumStorageLibrary", """
            using System;
            public enum Named : ulong { One = 1 }
            public static class Library
            {
                public static string[] Names<T>() where T : struct, Enum => Enum.GetNames<T>();
                public static T[] Values<T>() where T : struct, Enum => Enum.GetValues<T>();
            }
            """);
        ICompilationScenarioExecutor executor = new CompilationScenarioExecutor(assets);
        var observed = executor.Execute(new CompilationScenario(
            "EnumStorageApplication", $$"""
            using System;
            public static class EntryPoint
            {
                public static int Run(int input) => {{expression}};
            }
            """, "EntryPoint", true, WasmTarget.Wasm32, 1, [library]));

        Assert.Equal(1, observed);
    }

    [Theory]
    [InlineData(false, WasmTarget.Wasm32)]
    [InlineData(false, WasmTarget.Wasm64)]
    [InlineData(true, WasmTarget.Wasm32)]
    [InlineData(true, WasmTarget.Wasm64)]
    public void ClosedLibraryCallsKeepNamesSeparateFromNumericEnums(bool optimize, WasmTarget target)
    {
        using var assets = TestAssets.Create();
        const string librarySource = """
            using System;
            public enum Named : ulong { RetainedSecond = 2, RetainedFirst = 1 }
            public enum Numeric : ulong { PrunedNumericName = 1 }
            public static class Library
            {
                public static string[] Names<T>() where T : struct, Enum => Enum.GetNames<T>();
                public static T[] Values<T>() where T : struct, Enum => Enum.GetValues<T>();
            }
            """;
        const string source = """
            using System;
            public static class EntryPoint
            {
                public static int Run(int input)
                {
                    var names = Library.Names<Named>();
                    var values = Library.Values<Numeric>();
                    if (names.Length != 2 || names[0] != "RetainedFirst") return 1;
                    if (values.Length != 1 || values[0] != (Numeric)input) return 2;
                    if (!Enum.IsDefined((Numeric)input)) return 3;
                    if (((Enum)(Numeric)(ulong)input).GetHashCode() != input) return 4;
                    return Enum.GetName((Named)input) == names[0] ? 0 : 5;
                }
            }
            """;
        var library = optimize
            ? assets.CompileOptimizedSource("EnumDemandLibrary", librarySource)
            : assets.CompileSource("EnumDemandLibrary", librarySource);
        var assembly = optimize
            ? assets.CompileOptimizedSource("EnumDemandApplication", source, library)
            : assets.CompileSource("EnumDemandApplication", source, library);
        var compilation = NetWasmCompiler.Compile(new CompilerOptions(
            assembly, [assets.CoreLib, library], "EntryPoint", "Run", [], target));

        CompilerTestSupport.ValidateWithNode(compilation.ApplicationModule, assets.Directory);
        Assert.Equal(0, CompilerTestSupport.ExecuteWithStandardWasiNode(
            compilation.ApplicationModule, assets.Directory, 1, target, compilation.StaticDataEnd));
        Assert.True(compilation.ApplicationModule.AsSpan().IndexOf(
            Encoding.Unicode.GetBytes("RetainedSecond")) >= 0);
        Assert.True(compilation.ApplicationModule.AsSpan().IndexOf(
            Encoding.Unicode.GetBytes("PrunedNumericName")) < 0);
    }

    [Theory]
    [InlineData("parse", 1)]
    [InlineData("parse", 4)]
    [InlineData("parse", 8)]
    [InlineData("format", 1)]
    [InlineData("format", 4)]
    [InlineData("format", 8)]
    public void CompilerPreservesResultsAcrossIndependentUseSites(string operation, int sites)
    {
        using var assets = TestAssets.Create();
        var expression = operation == "parse"
            ? "(int)Enum.Parse<Flags>(input == 1 ? \"1\" : \"2\")"
            : "((Flags)(ulong)input).ToString().Length";
        var methods = string.Join("\n", Enumerable.Range(0, sites).Select(index =>
            $"private static int Use{index}(int input) => {expression};"));
        var calls = string.Join(" + ", Enumerable.Range(0, sites).Select(index =>
            $"Use{index}(input)"));
        var source = $$"""
            using System;
            [Flags]
            public enum Flags : ulong
            {
                RetainedFlag0 = 1, RetainedFlag1 = 2,
                RetainedFlag2 = 4, RetainedFlag3 = 8
            }
            public static class EntryPoint
            {
                public static int Run(int input) => {{calls}};
                {{methods}}
            }
            """;
        var assembly = assets.CompileOptimizedSource("EnumUseSiteFixture", source);
        var compilation = NetWasmCompiler.Compile(new CompilerOptions(
            assembly, [assets.CoreLib], "EntryPoint", "Run", []));

        CompilerTestSupport.ValidateWithNode(compilation.ApplicationModule, assets.Directory);
        Assert.Equal(sites * (operation == "parse" ? 1 : "RetainedFlag0".Length),
            CompilerTestSupport.ExecuteWithStandardWasiNode(
                compilation.ApplicationModule, assets.Directory, 1,
                WasmTarget.Wasm32, compilation.StaticDataEnd));
        output.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"enum-use-sites: operation={operation}; sites={sites}; module-bytes={compilation.ApplicationModule.Length}; static-end={compilation.StaticDataEnd}"));
    }

    public static IEnumerable<object[]> ScalingCases()
    {
        foreach (var operation in new[] { "numeric", "values", "names", "parse", "format" })
        {
            foreach (var bits in new[] { 1, 4, 8, 10, 11, 16, 32 })
            {
                yield return [operation, bits];
            }
        }
    }

    [Theory]
    [MemberData(nameof(ScalingCases))]
    public void CompilerPreservesResultsAcrossDeclaredMemberCounts(string operation, int bits)
    {
        using var assets = TestAssets.Create();
        var declarations = string.Join(",\n", Enumerable.Range(0, bits)
            .Select(index => $"RetainedFlag{index} = 1UL << {index}"));
        var expression = operation switch
        {
            "numeric" => "((Enum)(Flags)(ulong)input).GetHashCode()",
            "values" => "Enum.GetValues<Flags>().Length",
            "names" => "Enum.GetNames<Flags>().Length",
            "parse" => "(int)Enum.Parse<Flags>(input == 1 ? \"1\" : \"2\")",
            "format" => "((Flags)(ulong)input).ToString().Length",
            _ => throw new ArgumentOutOfRangeException(nameof(operation)),
        };
        var source = $$"""
            using System;
            [Flags]
            public enum Flags : ulong
            {
                {{declarations}}
            }
            public static class EntryPoint
            {
                public static int Run(int input) => {{expression}};
            }
            """;
        var assembly = assets.CompileOptimizedSource("EnumRetentionFixture", source);
        var compilation = NetWasmCompiler.Compile(new CompilerOptions(
            assembly, [assets.CoreLib], "EntryPoint", "Run", []));

        CompilerTestSupport.ValidateWithNode(compilation.ApplicationModule, assets.Directory);
        var expected = operation switch
        {
            "values" or "names" => bits,
            "format" => "RetainedFlag0".Length,
            _ => 1,
        };
        Assert.Equal(expected, CompilerTestSupport.ExecuteWithStandardWasiNode(
            compilation.ApplicationModule, assets.Directory, 1,
            WasmTarget.Wasm32, compilation.StaticDataEnd));

        var declaredNamesPresent = Enumerable.Range(0, bits).Count(index =>
            compilation.ApplicationModule.AsSpan().IndexOf(
                Encoding.Unicode.GetBytes($"RetainedFlag{index}")) >= 0);
        Assert.Equal(operation is "numeric" or "values" ? 0 : bits, declaredNamesPresent);
        output.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"enum-retention: operation={operation}; bits={bits}; module-bytes={compilation.ApplicationModule.Length}; static-end={compilation.StaticDataEnd}; declared-names-present={declaredNamesPresent}"));
    }

    private sealed class RecordingObserver : ICompilerMetricsObserver
    {
        public List<CompilerMetricsReport> Reports { get; } = [];

        public void Report(CompilerMetricsReport report) => Reports.Add(report);
    }
}
