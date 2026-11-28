// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.ComponentModel.Debugging;
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

    // An interface the world declares inline (api, run), one it names from an
    // unversioned package (my:ns/host, my:ns/math) and one it places under a
    // name of its own (calc) are each bound by their world key, the name the
    // component gives them, in the bindings and in the core module.
    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void InterfacesBindByTheirWorldKeys(WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var witPath = Path.Combine(assets.Root, "tests", "NetWasm.Compiler.Tests", "Fixtures", "world-keys.wit");
        using var services = CreateComponentModelServices();
        var document = services.GetRequiredService<IWitDocumentReader>().Read(witPath);
        var bindings = WitBindingCompositionRoot.Create().Generate(document, document.SelectWorld(null));
        foreach (var attribute in (string[])
                 [
                     "[WitImport(\"api\", \"some-func\")]", "[WitImport(\"my:ns/host\", \"hello\")]",
                     "[WitImport(\"calc\", \"add\")]", "[WitExport(\"run\", \"go\")]",
                     "[WitExport(\"my:ns/math\", \"add\")]",
                 ])
        {
            Assert.Contains(attribute, bindings, StringComparison.Ordinal);
        }

        var assembly = assets.CompileSource(
            "WorldKeysFixture",
            bindings +
            """

            public static partial class KeysExports
            {
                public static partial uint Go(uint x)
                {
                    ApiImports.SomeFunc(x);
                    return HostImports.Hello() + CalcImports.Add(x, 1);
                }

                public static partial uint Add(uint a, uint b) => a + b;
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
            [$"{prefix}|api some-func", $"{prefix}|calc add", $"{prefix}|my:ns/host hello"],
            result.FunctionImports.Where(import => import.Module.StartsWith(prefix + "|", StringComparison.Ordinal))
                .Select(import => $"{import.Module} {import.Name}").Order(StringComparer.Ordinal));
        var modulePath = Path.Combine(assets.Directory, $"world-keys-{target}.wasm");
        File.WriteAllBytes(modulePath, result.ApplicationModule);
        var tools = services.GetRequiredService<IWasmTools>();
        var validation = tools.Run("validate", modulePath, "--features", "all");
        Assert.True(validation.ExitCode == 0, validation.StandardError + validation.StandardOutput);
        var text = tools.Run("print", modulePath).StandardOutput;
        Assert.Contains($"(export \"{prefix}|run|go\"", text, StringComparison.Ordinal);
        Assert.Contains($"(export \"{prefix}|my:ns/math|add\"", text, StringComparison.Ordinal);
    }

    // Bindings generated before world keys name an interface package first;
    // the compiler says so instead of only that the binding is not in the world.
    [Fact]
    public void PackageFirstBindingKeysPointToRegeneratingTheBindings()
    {
        using var assets = TestAssets.Create();
        var witPath = Path.Combine(assets.Directory, "old-keys.wit");
        File.WriteAllText(witPath, """
            package docs:old@1.0.0;

            interface host {
              alpha: func() -> u32;
            }

            interface aux {
              zeta: func() -> u32;
            }

            world app {
              import host;
              import aux;
              export run: func() -> u32;
            }
            """);
        // The diagnostic reports the first binding by name, Alpha, with Alpha's
        // own interface, though Zeta's interface sorts first.
        var assembly = assets.CompileSource(
            "OldKeysFixture",
            """
            using System.Runtime.InteropServices.WebAssembly;

            public static class Bindings
            {
                [WitImport("docs:old@1.0.0/host", "alpha")]
                private static extern int Alpha();

                [WitImport("docs:old@1.0.0/aux", "zeta")]
                private static extern int Zeta();

                [WitExport("run")]
                public static int Run() => Alpha() + Zeta();
            }

            public static class EntryPoint
            {
                public static int Main(int input) => input;
            }
            """);

        var failure = Assert.Throws<CompilerException>(() => NetWasmCompiler.Compile(new CompilerOptions(
            assembly, [assets.CoreLib], "EntryPoint", "Main", [], WasmTarget.Wasm32, WitPath: witPath)));

        Assert.Contains("'docs:old@1.0.0/host' by the package-first key", failure.Message, StringComparison.Ordinal);
        Assert.Contains("Alpha", failure.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("Zeta", failure.Message, StringComparison.Ordinal);
        Assert.Contains("regenerate the bindings", failure.Message, StringComparison.Ordinal);
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
        RuntimeLayoutFiles.Write(layoutPath, result);
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
        var buildOutput = process.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken);
        var buildError = process.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken);
        await process.WaitForExitAsync(TestContext.Current.CancellationToken);
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
        Assert.Contains("wasi:cli/environment@0.3", wit.StandardOutput);
        Assert.DoesNotContain("preview1", wit.StandardOutput, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("wasi_unstable", wit.StandardOutput, StringComparison.OrdinalIgnoreCase);
    }

    // NetWasmDebugInfo keeps the application's function names through the merge and the optimizer into the component,
    // where a debugger and Wasmtime's trap backtraces read them; a product component names nothing.
    [Fact]
    public async Task DebugInformationKeepsManagedFunctionNamesInTheComponent()
    {
        using var assets = TestAssets.Create();
        var witPath = WitPath(assets);
        var assembly = CompileFixture(assets, "int");
        var result = NetWasmCompiler.Compile(new CompilerOptions(assembly, [assets.CoreLib], "EntryPoint", "Run", [],
            WitPath: witPath));
        var coreModulePath = Path.Combine(assets.Directory, "application.wasm");
        var layoutPath = Path.Combine(assets.Directory, "runtime-layout.json");
        var runtimePath = Path.Combine(assets.Directory, "runtime.wasm");
        File.WriteAllBytes(coreModulePath, result.ApplicationModule);
        RuntimeLayoutFiles.Write(layoutPath, result);
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
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Runtime build did not start");
        var buildOutput = process.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken);
        var buildError = process.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken);
        await process.WaitForExitAsync(TestContext.Current.CancellationToken);
        Assert.True(process.ExitCode == 0, await buildOutput + await buildError);
        using var services = CreateComponentModelServices();
        var packager = services.GetRequiredService<IComponentPackager>();
        var wasmTools = services.GetRequiredService<IWasmTools>();

        foreach (var (optimization, debugInfo) in new[]
        {
            (FinalWasmOptimization.O0, true),
            (FinalWasmOptimization.Oz, true),
            (FinalWasmOptimization.O0, false),
            (FinalWasmOptimization.Oz, false),
        })
        {
            var componentPath = Path.Combine(assets.Directory, $"application.{optimization}.{debugInfo}.component.wasm");
            packager.Package(new ComponentPackageRequest(coreModulePath, witPath, null, componentPath,
                ComponentTarget.Wasm32Wasi02, runtimePath, Optimization: optimization)
            { DebugInformation = debugInfo ? new() : null });
            var validation = wasmTools.Run("validate", componentPath, "--features", "all");
            Assert.True(validation.ExitCode == 0, validation.StandardError + validation.StandardOutput);
            // Optimizing inlines the trivial method, so the name section itself is what an optimized build keeps.
            var sections = wasmTools.Run("objdump", componentPath);
            Assert.Equal(0, sections.ExitCode);
            Assert.True(sections.StandardOutput.Contains("custom \"name\"", StringComparison.Ordinal) == debugInfo,
                $"{optimization} with debug information {debugInfo}:\n{sections.StandardOutput}");
            if (optimization == FinalWasmOptimization.O0)
            {
                Assert.Equal(debugInfo, File.ReadAllBytes(componentPath).AsSpan().IndexOf("EntryPoint::Run"u8) >= 0);
            }
        }
    }

    // With method symbols a Debug link gains DWARF that names the managed methods and maps their code to C# lines,
    // valid to LLVM's verifier, and a component's strip keeps it with the name section and drops everything else.
    [Fact]
    public async Task DebugInformationMapsTheLinkedCodeToItsSourceLines()
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSourceWithPortablePdb(
            "DebugLineFixture",
            """
            public static class EntryPoint
            {
                private static int Increment(int value) => value + 1;

                public static int Run(int value)
                {
                    var doubled = value * 2;
                    return Increment(doubled) + 1;
                }
            }
            """);
        var result = NetWasmCompiler.Compile(new CompilerOptions(assembly, [assets.CoreLib], "EntryPoint", "Run", [],
            Optimization: FinalWasmOptimization.O0, EmitStackTrace: true, ProjectDirectory: assets.Directory));
        var coreModulePath = Path.Combine(assets.Directory, "application.wasm");
        var symbolsPath = Path.Combine(assets.Directory, "application.wasm.method-symbols");
        var layoutPath = Path.Combine(assets.Directory, "runtime-layout.json");
        var runtimePath = Path.Combine(assets.Directory, "runtime.wasm");
        File.WriteAllBytes(coreModulePath, result.ApplicationModule);
        File.WriteAllBytes(symbolsPath, result.MethodSymbols!.Bytes);
        RuntimeLayoutFiles.Write(layoutPath, result);
        await BuildRuntimeAsync(assets, layoutPath, runtimePath);
        using var services = CreateComponentModelServices();
        var debug = new WasmDebugInformation(symbolsPath, assets.Directory);
        var linkedPath = Path.Combine(assets.Directory, "linked.wasm");
        services.GetRequiredService<NetWasm.Compiler.ComponentModel.Raw.IRawModuleLinker>().Link(
            new(coreModulePath, runtimePath, linkedPath, ComponentTarget.Wasm32Wasi02, FinalWasmOptimization.O0)
            {
                DebugInformation = debug,
            });

        var dwarfdump = Path.Combine(Environment.GetEnvironmentVariable("NETWASM_EMSDK_ROOT")!, "upstream", "bin",
            "llvm-dwarfdump");
        var verify = Run(dwarfdump, "--verify", linkedPath);
        Assert.True(verify.ExitCode == 0, verify.Output);
        var lineTable = Run(dwarfdump, "--debug-line", linkedPath).Output;
        Assert.Contains("name: \"DebugLineFixture.cs\"", lineTable, StringComparison.Ordinal);
        var lines = lineTable.Split('\n')
            .Select(line => System.Text.RegularExpressions.Regex.Match(line, @"^0x[0-9a-f]+\s+(\d+)\s+\d+\s+\d+\s"))
            .Where(match => match.Success)
            .Select(match => int.Parse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture))
            .ToHashSet();
        // Line 9, the closing brace, is where the folded return epilogue returns.
        Assert.True(new HashSet<int> { 3, 6, 7, 8, 9 }.IsSubsetOf(lines), lineTable);
        var information = Run(dwarfdump, "--debug-info", linkedPath).Output;
        Assert.Contains("DW_AT_name\t(\"EntryPoint.Run\")", information, StringComparison.Ordinal);
        Assert.Contains("DW_AT_name\t(\"EntryPoint.Increment\")", information, StringComparison.Ordinal);
        Assert.Contains($"DW_AT_comp_dir\t(\"{assets.Directory}\")", information, StringComparison.Ordinal);

        var wasmTools = services.GetRequiredService<IWasmTools>();
        // A stack-trace build keeps names out of the module (issue 225); debug information names its managed functions
        // in the name section, which the printer reads.
        Assert.Equal(-1, result.ApplicationModule.AsSpan().IndexOf("EntryPoint::Run"u8));
        var printed = wasmTools.Run("print", linkedPath).StandardOutput;
        Assert.Contains("(func $EntryPoint.Run ", printed, StringComparison.Ordinal);
        Assert.Contains("(func $EntryPoint.Increment ", printed, StringComparison.Ordinal);
        var tagged = Path.Combine(assets.Directory, "tagged.wasm");
        File.WriteAllBytes(tagged, [.. File.ReadAllBytes(linkedPath), .. CustomSection("producers"), .. CustomSection(".dwarf")]);
        var strippedPath = Path.Combine(assets.Directory, "stripped.wasm");
        Assert.Equal(0, wasmTools.Run("strip", "--delete", ComponentPackageExecution.NonDebugSections, tagged, "--output",
            strippedPath).ExitCode);
        var sections = wasmTools.Run("objdump", strippedPath).StandardOutput.Split('\n')
            .Where(line => line.TrimStart().StartsWith("custom ", StringComparison.Ordinal))
            .Select(line => line.Trim().Split('"')[1]);
        Assert.Equal([".debug_abbrev", ".debug_info", ".debug_line", "name"], sections.Order(StringComparer.Ordinal));
    }

    private static byte[] CustomSection(string name)
    {
        var bytes = Encoding.UTF8.GetBytes(name);
        return [0, (byte)(bytes.Length + 2), (byte)bytes.Length, .. bytes, 7];
    }

    private static async Task BuildRuntimeAsync(TestAssets assets, string layoutPath, string runtimePath)
    {
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
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Runtime build did not start");
        var buildOutput = process.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken);
        var buildError = process.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken);
        await process.WaitForExitAsync(TestContext.Current.CancellationToken);
        Assert.True(process.ExitCode == 0, await buildOutput + await buildError);
    }

    private static (int ExitCode, string Output) Run(string fileName, params string[] arguments)
    {
        var start = new ProcessStartInfo(fileName)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new InvalidOperationException($"{fileName} did not start");
        var error = process.StandardError.ReadToEndAsync();
        var output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        return (process.ExitCode, output + error.Result);
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
