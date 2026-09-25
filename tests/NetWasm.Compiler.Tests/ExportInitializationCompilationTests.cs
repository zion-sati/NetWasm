using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

using static CompilerTestSupport;

public sealed class ExportInitializationCompilationTests
{
    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void RequestedExportCanBeTheFirstManagedEntryBoundaryInvoked(WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource(
            "ExportInitializationFixture",
            """
            namespace ExportInitializationFixture;

            public sealed class Box(int value)
            {
                public int Value { get; } = value;
            }

            public static class EntryPoint
            {
                public static int Run(int input) => input;
                public static int Allocate(int input) => new Box(input).Value;
            }
            """);
        var compilation = NetWasmCompiler.Compile(new CompilerOptions(
            assembly,
            [assets.CoreLib],
            "ExportInitializationFixture.EntryPoint",
            "Run",
            [new RequestedExport(
                "allocate",
                "ExportInitializationFixture.EntryPoint",
                "Allocate")],
            Target: target));

        Assert.Equal(41, ExecuteWithStandardWasiNode(
            compilation.ApplicationModule,
            assets.Directory,
            41,
            target,
            compilation.StaticDataEnd,
            entryExportName: "allocate"));
    }

    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void RuntimeRegistrationRemainsIdempotentAcrossPublicEntryBoundaries(WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource(
            "RepeatedExportInitializationFixture",
            """
            namespace RepeatedExportInitializationFixture;

            public sealed class Box(int value)
            {
                public int Value { get; } = value;
            }

            public static class EntryPoint
            {
                public static int Run(int input) => input;
                public static int Allocate(int input) => new Box(input).Value;
            }
            """);
        var compilation = NetWasmCompiler.Compile(new CompilerOptions(
            assembly,
            [assets.CoreLib],
            "RepeatedExportInitializationFixture.EntryPoint",
            "Run",
            [new RequestedExport(
                "allocate",
                "RepeatedExportInitializationFixture.EntryPoint",
                "Allocate")],
            Target: target));

        Assert.Equal(41, ExecuteWithStandardWasiNode(
            compilation.ApplicationModule,
            assets.Directory,
            41,
            target,
            compilation.StaticDataEnd,
            environment: new Dictionary<string, string>(),
            timeZoneAssetPath: null,
            entryInvocationCount: 2,
            entryExportName: "allocate"));
    }
}
