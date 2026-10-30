using Microsoft.Extensions.DependencyInjection;
using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

public sealed class WasiHttpPreview2BindingCompilationTests
{
    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void OfficialHttpAndIoBindingsCompileAsOrdinaryLatestCSharp(WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var witPath = Path.Combine(
            assets.Root,
            "tests",
            "end-to-end",
            "component-model",
            "fixtures",
            "wasi-http-wit");
        using var services = CreateComponentModelServices();
        var documentReader = services.GetRequiredService<IWitDocumentReader>();
        var bindingGenerator = WitBindingCompositionRoot.Create();
        var document = documentReader.Read(witPath);
        var bindings = bindingGenerator.Generate(
            document,
            document.SelectWorld("netwasm:http-spike/outgoing-http-spike@1.0.0"));

        var assembly = assets.CompileSource(
            "WasiHttpBindingFixture",
            bindings +
            """

            public static class EntryPoint
            {
                public static int Run(int input) => input;
            }
            """);

        _ = NetWasmCompiler.Compile(new CompilerOptions(
            assembly,
            [assets.CoreLib],
            "NetWasm.Wit.Netwasm.Http.Spike._1._0._0.EntryPoint",
            "Run",
            [],
            target,
            WitPath: witPath,
            WitWorld: "netwasm:http-spike/outgoing-http-spike@1.0.0"));
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
