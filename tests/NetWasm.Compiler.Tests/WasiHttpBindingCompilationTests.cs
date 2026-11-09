using Microsoft.Extensions.DependencyInjection;
using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

public sealed class WasiHttpBindingCompilationTests
{
    private const string World = "wasi:http/service@0.3.2";

    // The official wasi:http 0.3 service world binds as ordinary C#: its client import's async
    // send, the request and response resources with their body streams and trailer futures,
    // and the handler export that receives an imported resource. A handler that only answers
    // compiles for both widths.
    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void OfficialHttpServiceBindingsCompileAsOrdinaryLatestCSharp(WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var witPath = HttpWit(assets);
        using var services = CreateComponentModelServices();
        var document = services.GetRequiredService<IWitDocumentReader>().Read(witPath);
        var bindings = WitBindingCompositionRoot.Create().Generate(document, document.SelectWorld(World));

        var assembly = assets.CompileSource(
            "WasiHttpBindingFixture",
            bindings +
            """

            public static partial class ServiceExports
            {
                public static partial global::System.Threading.Tasks.Task<WitResult<Response, WasiHttpTypesErrorCode>> Handle(Request request)
                {
                    request.Dispose();
                    var (response, _) = WasiHttpTypesImports.ResponseNew(
                        WasiHttpTypesImports.FromList([]).Ok,
                        WitOption<global::System.IO.Stream>.None,
                        global::System.Threading.Tasks.Task.FromResult(
                            WitResult<WitOption<Fields>, WasiHttpTypesErrorCode>.FromOk(WitOption<Fields>.None)));
                    return global::System.Threading.Tasks.Task.FromResult(
                        WitResult<Response, WasiHttpTypesErrorCode>.FromOk(response));
                }
            }

            public static class EntryPoint
            {
                public static int Run(int input) => input;
            }
            """);

        _ = NetWasmCompiler.Compile(new CompilerOptions(
            assembly,
            [assets.CoreLib],
            "NetWasm.Wit.Wasi.Http._0._3._2.EntryPoint",
            "Run",
            [],
            target,
            WitPath: witPath,
            WitWorld: World));
    }

    // The http package with the packages it depends on, as wit-parser resolves a directory.
    private static string HttpWit(TestAssets assets)
    {
        var source = Path.Combine(assets.Root, "wit", "wasi-0.3.2");
        var root = Path.Combine(assets.Directory, "wasi-http-wit");
        Directory.CreateDirectory(root);
        foreach (var file in Directory.GetFiles(Path.Combine(source, "http"), "*.wit"))
        {
            File.Copy(file, Path.Combine(root, Path.GetFileName(file)));
        }

        foreach (var package in new[] { "cli", "clocks", "filesystem", "random", "sockets" })
        {
            var destination = Path.Combine(root, "deps", "wasi-" + package);
            Directory.CreateDirectory(destination);
            foreach (var file in Directory.GetFiles(Path.Combine(source, package), "*.wit"))
            {
                File.Copy(file, Path.Combine(destination, Path.GetFileName(file)));
            }
        }

        return root;
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
