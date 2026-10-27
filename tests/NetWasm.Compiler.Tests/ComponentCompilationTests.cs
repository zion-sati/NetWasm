using System.Diagnostics;
using System.Text;
using System.Text.Json;
using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.Core;
using Microsoft.Extensions.DependencyInjection;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

public sealed class ComponentCompilationTests
{
    [Theory]
    [InlineData(WasmTarget.Wasm32, "cm32p2")]
    [InlineData(WasmTarget.Wasm64, "cm64p2")]
    public void SelectedWitWorldDrivesScalarCanonicalBoundary(
        WasmTarget target,
        string prefix)
    {
        using var assets = TestAssets.Create();
        var assembly = CompileFixture(assets, "int");
        var result = NetWasmCompiler.Compile(new CompilerOptions(
            assembly,
            [assets.CoreLib],
            "EntryPoint",
            "Run",
            [],
            target,
            WitPath: WitPath(assets)));
        var moduleText = Encoding.UTF8.GetString(result.ApplicationModule);

        Assert.Contains(prefix, moduleText, StringComparison.Ordinal);
        Assert.Contains("increment", moduleText, StringComparison.Ordinal);
        Assert.Contains($"{prefix}||run", moduleText, StringComparison.Ordinal);
        Assert.Contains($"{prefix}||run_post", moduleText, StringComparison.Ordinal);
        Assert.Contains($"{prefix}_memory", moduleText, StringComparison.Ordinal);
        Assert.Contains($"{prefix}_realloc", moduleText, StringComparison.Ordinal);
        Assert.DoesNotContain("netwasm.platform", moduleText, StringComparison.Ordinal);
    }

    // Issue #120: the selected world's exports may be implemented in a
    // referenced library, like JSExports.
    [Theory]
    [InlineData(WasmTarget.Wasm32, "cm32p2")]
    [InlineData(WasmTarget.Wasm64, "cm64p2")]
    public void WorldExportsImplementedInAReferencedLibraryAreExported(
        WasmTarget target,
        string prefix)
    {
        using var assets = TestAssets.Create();
        var library = assets.CompileSource(
            "ScalarComponentLibrary",
            """
            using System.Runtime.InteropServices.WebAssembly;

            public static class LibraryExports
            {
                [WitImport("increment")]
                private static extern int Increment(int value);

                [WitExport("run")]
                public static int Run(int value) => Increment(value);
            }
            """);
        var assembly = assets.CompileSource(
            "ScalarComponentApplication",
            """
            public static class EntryPoint
            {
                public static int Run(int value) => value;
            }
            """,
            library);

        var result = NetWasmCompiler.Compile(new CompilerOptions(
            assembly,
            [assets.CoreLib, library],
            "EntryPoint",
            "Run",
            [],
            target,
            WitPath: WitPath(assets)));
        var moduleText = Encoding.UTF8.GetString(result.ApplicationModule);

        Assert.Contains($"{prefix}||run", moduleText, StringComparison.Ordinal);
        Assert.Contains($"{prefix}||run_post", moduleText, StringComparison.Ordinal);
        Assert.Contains("increment", moduleText, StringComparison.Ordinal);
    }

    // A library's generated bindings carry both the export and its
    // post-return; the world binds both from the referenced assembly.
    [Fact]
    public void WorldExportAndPostReturnImplementedInAReferencedLibraryAreBound()
    {
        using var assets = TestAssets.Create();
        var witPath = Path.Combine(
            assets.Root,
            "tests",
            "NetWasm.Compiler.Tests",
            "Fixtures",
            "string-component.wit");
        using var services = CreateComponentModelServices();
        var document = services.GetRequiredService<IWitDocumentReader>().Read(witPath);
        var bindings = WitBindingCompositionRoot.Create().Generate(document, document.SelectWorld(null));
        var library = assets.CompileSource(
            "StringComponentLibrary",
            bindings +
            """

            public static partial class StringsExports
            {
                public static partial string EchoBack(string value) => value;
            }
            """);
        var assembly = assets.CompileSource(
            "StringComponentApplication",
            """
            public static class EntryPoint
            {
                public static int Run(int value) => value;
            }
            """,
            library);

        var result = NetWasmCompiler.Compile(new CompilerOptions(
            assembly,
            [assets.CoreLib, library],
            "EntryPoint",
            "Run",
            [],
            WitPath: witPath));
        var moduleText = Encoding.UTF8.GetString(result.ApplicationModule);

        Assert.Contains("cm32p2||echo-back", moduleText, StringComparison.Ordinal);
        Assert.Contains("cm32p2||echo-back_post", moduleText, StringComparison.Ordinal);
        Assert.Contains(result.Program.Methods.Values, body =>
            body.Method.Definition.Key.Assembly.Name == "StringComponentLibrary" &&
            body.Method.Definition.Name.EndsWith("Post", StringComparison.Ordinal));
    }

    [Fact]
    public void ReachableWitBindingRequiresSelectedWorld()
    {
        using var assets = TestAssets.Create();
        var assembly = CompileFixture(assets, "int");

        var exception = Assert.Throws<CompilerException>(() =>
            NetWasmCompiler.Compile(new CompilerOptions(
                assembly,
                [assets.CoreLib],
                "EntryPoint",
                "Run",
                [])));

        Assert.Equal(DiagnosticCode.ComponentContract, exception.Diagnostic.Code);
        Assert.Contains("require '--wit'", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ManagedSignatureMustMatchSelectedWitFunction()
    {
        using var assets = TestAssets.Create();
        var assembly = CompileFixture(assets, "long");

        var exception = Assert.Throws<CompilerException>(() =>
            NetWasmCompiler.Compile(new CompilerOptions(
                assembly,
                [assets.CoreLib],
                "EntryPoint",
                "Run",
                [],
                WitPath: WitPath(assets))));

        Assert.Equal(DiagnosticCode.ComponentContract, exception.Diagnostic.Code);
        Assert.Contains("requires canonical core signature", exception.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public void GeneratedStringAdaptersCompileIntoCanonicalBoundary()
    {
        using var assets = TestAssets.Create();
        var witPath = Path.Combine(
            assets.Root,
            "tests",
            "NetWasm.Compiler.Tests",
            "Fixtures",
            "string-component.wit");
        using var services = CreateComponentModelServices();
        var documentReader = services.GetRequiredService<IWitDocumentReader>();
        var bindingGenerator = WitBindingCompositionRoot.Create();
        var document = documentReader.Read(witPath);
        var bindings = bindingGenerator.Generate(document, document.SelectWorld(null));
        var assembly = assets.CompileSource(
            "StringComponentFixture",
            bindings +
            """

            public static partial class StringsExports
            {
                public static partial string EchoBack(string value) => value;
            }

            public static class EntryPoint
            {
                public static int Run(int value) => StringsImports.Echo("hello").Length + value;
            }
            """);

        var result = NetWasmCompiler.Compile(new CompilerOptions(
            assembly,
            [assets.CoreLib],
            "NetWasm.Wit.Example.Strings._1._0._0.EntryPoint",
            "Run",
            [],
            WitPath: witPath));
        var moduleText = Encoding.UTF8.GetString(result.ApplicationModule);

        Assert.Contains("cm32p2||echo", moduleText, StringComparison.Ordinal);
        Assert.Contains("cm32p2||echo-back", moduleText, StringComparison.Ordinal);
        Assert.Contains("cm32p2||echo-back_post", moduleText, StringComparison.Ordinal);
    }

    // A world may declare its own types, as `type` and `enum` items, and use
    // them in the functions it imports.
    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void WorldDeclaredTypesCompileIntoTheCanonicalBoundary(WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var witPath = Path.Combine(assets.Root, "tests", "NetWasm.Compiler.Tests", "Fixtures", "world-types.wit");
        using var services = CreateComponentModelServices();
        var document = services.GetRequiredService<IWitDocumentReader>().Read(witPath);
        var bindings = WitBindingCompositionRoot.Create().Generate(document, document.SelectWorld(null));
        Assert.Contains("public enum MyEnum", bindings, StringComparison.Ordinal);

        var assembly = assets.CompileSource(
            "WorldTypesFixture",
            bindings +
            """

            public static partial class MyWorldExports
            {
                public static partial void MyExport()
                {
                    if (MyWorldImports.MyFnA(MyWorldImports.MyFnB() + 1) == MyEnum.D)
                    {
                        _ = MyWorldImports.MyFnB();
                    }
                }
            }

            public static class EntryPoint
            {
                public static int Main(int input) => input;
            }
            """);
        var result = NetWasmCompiler.Compile(new CompilerOptions(
            assembly,
            [assets.CoreLib],
            "NetWasm.Wit.My.Ns.EntryPoint",
            "Main",
            [],
            target,
            WitPath: witPath));

        var modulePath = Path.Combine(assets.Directory, $"world-types-{target}.wasm");
        File.WriteAllBytes(modulePath, result.ApplicationModule);
        var validation = services.GetRequiredService<IWasmTools>().Run("validate", modulePath, "--features", "all");
        Assert.True(validation.ExitCode == 0, validation.StandardError + validation.StandardOutput);
    }

    // A world may declare a resource itself; its functions are the world's own
    // imports and its drop comes from the root module.
    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void WorldDeclaredResourcesCompileIntoTheCanonicalBoundary(WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var witPath = Path.Combine(assets.Root, "tests", "NetWasm.Compiler.Tests", "Fixtures", "world-resources.wit");
        using var services = CreateComponentModelServices();
        var document = services.GetRequiredService<IWitDocumentReader>().Read(witPath);
        var bindings = WitBindingCompositionRoot.Create().Generate(document, document.SelectWorld(null));
        Assert.Contains("[WitResource(\"counter\")]", bindings, StringComparison.Ordinal);

        var assembly = assets.CompileSource(
            "WorldResourcesFixture",
            bindings +
            """

            public static partial class CounterWorldExports
            {
                public static partial uint Run()
                {
                    using var counter = CounterWorldImports.CreateCounter(1);
                    using var made = CounterWorldImports.MakeCounter(2);
                    return CounterWorldImports.Increment(counter) + CounterWorldImports.Read(made) + CounterWorldImports.Total();
                }
            }

            public static class EntryPoint
            {
                public static int Main(int input) => input;
            }
            """);
        var result = NetWasmCompiler.Compile(new CompilerOptions(
            assembly,
            [assets.CoreLib],
            "NetWasm.Wit.My.Ns.EntryPoint",
            "Main",
            [],
            target,
            WitPath: witPath));

        var prefix = target == WasmTarget.Wasm64 ? "cm64p2" : "cm32p2";
        Assert.Equal(
            ["[constructor]counter", "[method]counter.increment", "[static]counter.total", "counter_drop", "make-counter", "read"],
            result.FunctionImports.Where(import => import.Module == prefix).Select(import => import.Name).Order(StringComparer.Ordinal));
        var modulePath = Path.Combine(assets.Directory, $"world-resources-{target}.wasm");
        File.WriteAllBytes(modulePath, result.ApplicationModule);
        var validation = services.GetRequiredService<IWasmTools>().Run("validate", modulePath, "--features", "all");
        Assert.True(validation.ExitCode == 0, validation.StandardError + validation.StandardOutput);
    }

    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void GeneratedCompositeBindingsCompileAsOrdinaryLatestCSharp(
        WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var witPath = Path.Combine(
            assets.Root,
            "tests",
            "NetWasm.Compiler.Tests",
            "Fixtures",
            "composite-component.wit");
        using var services = CreateComponentModelServices();
        var documentReader = services.GetRequiredService<IWitDocumentReader>();
        var bindingGenerator = WitBindingCompositionRoot.Create();
        var wasmTools = services.GetRequiredService<IWasmTools>();
        var document = documentReader.Read(witPath);
        var bindings = bindingGenerator.Generate(document, document.SelectWorld(null))
            .ReplaceLineEndings("\r\n");

        var assembly = assets.CompileSource(
            "CompositeComponentFixture",
            bindings +
            """

            public static partial class TestCanonicalExports
            {
                public static partial ScalarValues RoundTripScalars(
                    ScalarValues value) => value;

                public static partial uint BoundaryProbes() => 0;

                public static partial Mode Select(Mode mode, Permissions permissions) => mode;

                public static partial WidePermissions Widen(WidePermissions value) => value;

                public static partial WitResult<(uint, string), string> Transform(
                    Item[] values,
                    WitOption<Choice> selected,
                    Permissions permissions) =>
                    WitResult<(uint, string), string>.FromOk(
                        ((uint)values.Length, selected.HasValue ? "some" : "none"));
            }

            public static class EntryPoint
            {
                public static int Main(int input)
                {
                    var items = new Item[] { new("hello", 7) };
                    var result = HostImports.Transform(
                        items,
                        WitOption<Choice>.None,
                        Permissions.Read);
                    return result.IsOk
                        ? (int)result.Ok.Item1 + input
                        : result.Error.Length + input;
                }
            }
            """);
        var generatedNamespace = bindings.Split(["\r\n", "\n"], StringSplitOptions.None)
            .Single(line => line.StartsWith("namespace ", StringComparison.Ordinal))[10..^1];

        var result = NetWasmCompiler.Compile(new CompilerOptions(
            assembly,
            [assets.CoreLib],
            generatedNamespace + ".EntryPoint",
            "Main",
            [],
            target,
            WitPath: witPath));

        Assert.NotEmpty(result.ApplicationModule);
        var modulePath = Path.Combine(assets.Directory, $"composite-{target}.wasm");
        File.WriteAllBytes(modulePath, result.ApplicationModule);
        var validation = wasmTools.Run(
            "validate", modulePath, "--features", "all");
        Assert.True(
            validation.ExitCode == 0,
            validation.StandardError + validation.StandardOutput);
    }

    [Fact]
    public async Task OrdinaryManagedApplicationPackagesAsAValidatedComponent()
    {
        using var assets = TestAssets.Create();
        var witPath = WitPath(assets);
        var assembly = CompileFixture(assets, "int");
        var result = NetWasmCompiler.Compile(new CompilerOptions(
            assembly,
            [assets.CoreLib],
            "EntryPoint",
            "Run",
            [],
            WitPath: witPath));
        var coreModulePath = Path.Combine(assets.Directory, "application.wasm");
        var componentPath = Path.Combine(assets.Directory, "application.component.wasm");
        var repeatedComponentPath = Path.Combine(
            assets.Directory,
            "application.repeated.component.wasm");
        var layoutPath = Path.Combine(assets.Directory, "runtime-layout.json");
        var runtimePath = Path.Combine(assets.Directory, "runtime.wasm");
        File.WriteAllBytes(coreModulePath, result.ApplicationModule);
        File.WriteAllText(layoutPath, JsonSerializer.Serialize(new
        {
            schemaVersion = 2,
            target = "wasm32",
            applicationStaticDataEnd = result.StaticDataEnd,
        }));
        var start = new ProcessStartInfo("bash")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            ArgumentList =
            {
                Path.Combine(assets.Root, "eng", "build-netwasm-runtime.sh"),
                "--runtime-layout", layoutPath,
                "--output", runtimePath,
                "--target", "wasm32",
                "--configuration", "release",
            },
        };
        using var process = Process.Start(start)
            ?? throw new InvalidOperationException("Runtime build did not start");
        var buildOutput = process.StandardOutput.ReadToEndAsync();
        var buildError = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        Assert.True(process.ExitCode == 0, await buildOutput + await buildError);
        using var services = CreateComponentModelServices();
        var packager = services.GetRequiredService<IComponentPackager>();
        var wasmTools = services.GetRequiredService<IWasmTools>();
        var request = new ComponentPackageRequest(
            coreModulePath,
            witPath,
            null,
            componentPath,
            ComponentTarget.Wasm32Wasi02,
            runtimePath,
            Optimization: FinalWasmOptimization.Oz);
        packager.Package(request);
        packager.Package(request with { OutputPath = repeatedComponentPath });

        Assert.True(File.Exists(componentPath));
        Assert.Equal(
            File.ReadAllBytes(componentPath),
            File.ReadAllBytes(repeatedComponentPath));
        var validation = wasmTools.Run(
            "validate", componentPath, "--features", "all");
        Assert.True(
            validation.ExitCode == 0,
            validation.StandardError + validation.StandardOutput);
        var wit = wasmTools.Run("component", "wit", componentPath);
        Assert.Equal(0, wit.ExitCode);
        Assert.Contains("import increment: func(value: s32) -> s32", wit.StandardOutput);
        Assert.Contains("export run: func(value: s32) -> s32", wit.StandardOutput);
        Assert.Contains("wasi:cli/environment@0.2", wit.StandardOutput);
        Assert.DoesNotContain("preview1", wit.StandardOutput, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("wasi_unstable", wit.StandardOutput, StringComparison.OrdinalIgnoreCase);
    }

    private static string CompileFixture(TestAssets assets, string importType) =>
        assets.CompileSource(
            "ScalarComponentFixture" + importType,
            $$"""
            using System.Runtime.InteropServices.WebAssembly;

            public static class EntryPoint
            {
                [WitImport("increment")]
                private static extern {{importType}} Increment({{importType}} value);

                [WitExport("run")]
                public static int Run(int value) => (int)Increment(value);
            }
            """);

    private static string WitPath(TestAssets assets) => Path.Combine(
        assets.Root,
        "tests",
        "NetWasm.Compiler.Tests",
        "Fixtures",
        "scalar-component.wit");

    private static ServiceProvider CreateComponentModelServices() =>
        new ServiceCollection()
            .AddNetWasmCompiler()
            .BuildServiceProvider(new ServiceProviderOptions
            {
                ValidateOnBuild = true,
                ValidateScopes = true,
            });
}
