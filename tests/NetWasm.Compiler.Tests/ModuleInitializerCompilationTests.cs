using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

public sealed class ModuleInitializerCompilationTests
{
    [Theory]
    [InlineData(false, WasmTarget.Wasm32)]
    [InlineData(false, WasmTarget.Wasm64)]
    [InlineData(true, WasmTarget.Wasm32)]
    [InlineData(true, WasmTarget.Wasm64)]
    public void CompilerExecutesOrdinaryModuleInitializersOnceBeforeManagedEntryPoints(
        bool optimize,
        WasmTarget target)
    {
        using var assets = TestAssets.Create();
        ICompilationScenarioExecutor executor = new CompilationScenarioExecutor(assets);
        var observed = executor.Execute(new CompilationScenario(
            "ModuleInitializerFixture",
            """
            using System.Runtime.CompilerServices;

            namespace ModuleInitializerFixture;

            public static class State
            {
                public static int Value;
            }

            public static class Bootstrap
            {
                [ModuleInitializer]
                public static void Initialize() => State.Value += 40;
            }

            public static class EntryPoint
            {
                public static int Run(int input) => State.Value + input;
            }
            """,
            "ModuleInitializerFixture.EntryPoint",
            optimize,
            target,
            2,
            [])
        {
            EntryInvocationCount = 2,
        });

        Assert.Equal(42, observed);
    }

    [Theory]
    [InlineData(false, WasmTarget.Wasm32)]
    [InlineData(false, WasmTarget.Wasm64)]
    [InlineData(true, WasmTarget.Wasm32)]
    [InlineData(true, WasmTarget.Wasm64)]
    public void CompilerExecutesModuleInitializerFromReachableLibraryAssembly(
        bool optimize,
        WasmTarget target)
    {
        using var assets = TestAssets.Create();
        const string librarySource = """
            using System.Runtime.CompilerServices;

            namespace ModuleInitializerLibrary;

            public static class LibraryState
            {
                public static int Value;
            }

            public static class Bootstrap
            {
                [ModuleInitializer]
                public static void Initialize() => LibraryState.Value = 40;
            }
            """;
        var library = optimize
            ? assets.CompileOptimizedSource("ModuleInitializerLibrary", librarySource)
            : assets.CompileSource("ModuleInitializerLibrary", librarySource);
        const string applicationSource = """
            using System.Runtime.CompilerServices;
            using ModuleInitializerLibrary;

            namespace ModuleInitializerApplication;

            public static class State
            {
                public static int Value;
            }

            public static class Bootstrap
            {
                [ModuleInitializer]
                public static void Initialize() => State.Value = LibraryState.Value + 1;
            }

            public static class EntryPoint
            {
                public static int Run(int input) => State.Value + input;
            }
            """;
        ICompilationScenarioExecutor executor = new CompilationScenarioExecutor(assets);
        var observed = executor.Execute(new CompilationScenario(
            "ModuleInitializerApplication",
            applicationSource,
            "ModuleInitializerApplication.EntryPoint",
            optimize,
            target,
            1,
            [library])
        {
            EntryInvocationCount = 2,
        });

        Assert.Equal(42, observed);
    }
}
