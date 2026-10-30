using Microsoft.Extensions.DependencyInjection;
using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

public sealed class NestedCanonicalShapeBindingCompilationTests
{
    private const string World = "netwasm:binding-shapes/imports@1.0.0";

    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void NestedCanonicalShapesCompileAsOrdinaryLatestCSharp(
        WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var witPath = Path.Combine(
            assets.Root,
            "tests",
            "end-to-end",
            "component-model",
            "fixtures",
            "nested-canonical-shapes-wit");
        using var services = CreateComponentModelServices();
        var documentReader = services.GetRequiredService<IWitDocumentReader>();
        var bindingGenerator = WitBindingCompositionRoot.Create();
        var document = documentReader.Read(witPath);
        var bindings = bindingGenerator.Generate(
            document,
            document.SelectWorld(World));
        var assembly = assets.CompileSource(
            "NestedCanonicalShapeBindingFixture",
            bindings +
            """

            public static class EntryPoint
            {
                public static int Run(int input) => input;
            }
            """);

        var result = NetWasmCompiler.Compile(new CompilerOptions(
            assembly,
            [assets.CoreLib],
            "NetWasm.Wit.Netwasm.Binding.Shapes._1._0._0.EntryPoint",
            "Run",
            [],
            target,
            WitPath: witPath,
            WitWorld: World));

        Assert.NotEmpty(result.ApplicationModule);
    }

    private static ServiceProvider CreateComponentModelServices() =>
        new ServiceCollection()
            .AddNetWasmCompiler()
            .BuildServiceProvider(new ServiceProviderOptions
            {
                ValidateOnBuild = true,
                ValidateScopes = true,
            });
}
