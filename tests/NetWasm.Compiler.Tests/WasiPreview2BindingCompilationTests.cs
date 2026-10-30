using System.Text;
using Microsoft.Extensions.DependencyInjection;
using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

public sealed class WasiPreview2BindingCompilationTests
{
    [Theory]
    [InlineData(WasmTarget.Wasm32, "cm32p2")]
    [InlineData(WasmTarget.Wasm64, "cm64p2")]
    public void OfficialClockAndPollBindingsCompileAsOrdinaryLatestCSharp(
        WasmTarget target,
        string canonicalPrefix)
    {
        using var assets = TestAssets.Create();
        var witPath = Path.Combine(
            assets.Root,
            "wit",
            "wasi-0.2.11",
            "clocks");
        using var services = CreateComponentModelServices();
        var documentReader = services.GetRequiredService<IWitDocumentReader>();
        var bindingGenerator = WitBindingCompositionRoot.Create();
        var document = documentReader.Read(witPath);
        var bindings = bindingGenerator.Generate(
            document,
            document.SelectWorld("wasi:clocks/imports@0.2.11"));
        var assembly = assets.CompileSource(
            "WasiClockBindingFixture",
            bindings +
            """

            public static class EntryPoint
            {
                public static int Run(int input)
                {
                    var wall = WallClockImports.Now();
                    using var pollable = MonotonicClockImports.SubscribeDuration(
                        unchecked((ulong)input));
                    var ready = PollImports.Ready(pollable);
                    return unchecked((int)wall.Seconds)
                        + unchecked((int)MonotonicClockImports.Now())
                        + (ready ? 1 : 0);
                }
            }
            """);

        var result = NetWasmCompiler.Compile(new CompilerOptions(
            assembly,
            [assets.CoreLib],
            "NetWasm.Wit.Wasi.Clocks._0._2._11.EntryPoint",
            "Run",
            [],
            target,
            WitPath: witPath,
            WitWorld: "wasi:clocks/imports@0.2.11"));
        var module = Encoding.UTF8.GetString(result.ApplicationModule);
        var imports = result.Program.WitImportMethods
            .Select(method => method.WitImport!)
            .ToArray();

        Assert.Contains(imports, import =>
            import.InterfaceName == "wasi:clocks/wall-clock@0.2.11" &&
            import.FunctionName == "now");
        Assert.Contains(imports, import =>
            import.InterfaceName == "wasi:clocks/monotonic-clock@0.2.11" &&
            import.FunctionName == "subscribe-duration");
        Assert.Contains(imports, import =>
            import.InterfaceName == "wasi:io/poll@0.2.11" &&
            import.FunctionName == "[method]pollable.ready");
        Assert.Contains(canonicalPrefix, module, StringComparison.Ordinal);
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
