using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Core.ManagedExecutables;
using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.ComponentModel.Worlds;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

public sealed class ManagedExecutableAsyncMainCompilationTests
{
    public static TheoryData<bool, WasmTarget> Configurations => new()
    {
        { false, WasmTarget.Wasm32 },
        { true, WasmTarget.Wasm32 },
        { false, WasmTarget.Wasm64 },
        { true, WasmTarget.Wasm64 },
    };

    [Theory]
    [MemberData(nameof(Configurations))]
    public void CompilerResumesAsynchronousManagedEntryPoint(
        bool optimize,
        WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var assemblyPath = CompileExecutable(assets, optimize);
        var entryPoint = ReadEntryPoint(assemblyPath);

        var compilation = NetWasmCompiler.Compile(new CompilerOptions(
            assemblyPath,
            [assets.CoreLib],
            entryPoint.TypeName,
            entryPoint.MethodName,
            [],
            target,
            WitPath: Path.Combine(
                assets.Root,
                "wit",
                "netwasm-platform-1.0.0"),
            WitWorld: "netwasm:platform/async-platform@1.0.0",
            EntryMethodToken: entryPoint.Token,
            EntryPointKind: CompilerEntryPointKind.ManagedExecutable));

        var asyncBinding = Assert.Single(
            compilation.Program.JavaScriptAsyncBindings);
        Assert.Equal(compilation.Program.EntryPoint!.Key, asyncBinding.Key);
        Assert.True(asyncBinding.Value.Return.HasResult);

        CompilerTestSupport.ValidateWithNode(
            compilation.ApplicationModule,
            assets.Directory);
        var result = CompilerTestSupport.ExecuteWithStandardWasiNode(
            compilation.ApplicationModule,
            assets.Directory,
            0,
            target,
            compilation.StaticDataEnd,
            drainReactor: true,
            entryExportName: "run",
            observeAsyncProcess: true);

        Assert.Equal(0, result);
    }

    public static TheoryData<bool, bool, bool, int> ComponentConfigurations
    {
        get
        {
            var configurations = new TheoryData<bool, bool, bool, int>();
            foreach (var optimize in new[] { false, true })
                foreach (var returnsExitCode in new[] { false, true })
                    foreach (var hasArguments in new[] { false, true })
                        foreach (var status in new[] { 1, 2, 3 })
                            configurations.Add(optimize, returnsExitCode, hasArguments, status);
            return configurations;
        }
    }

    [Theory]
    [MemberData(nameof(ComponentConfigurations))]
    public async Task AsynchronousMainCompletesThroughTheComponentProcessContract(
        bool optimize, bool returnsExitCode, bool hasArguments, int expectedStatus)
    {
        using var assets = TestAssets.Create();
        var completion = expectedStatus switch
        {
            1 => returnsExitCode ? "return 17;" : "return;",
            2 => "throw new System.InvalidOperationException();",
            3 => "throw new System.OperationCanceledException();",
            _ => throw new ArgumentOutOfRangeException(nameof(expectedStatus)),
        };
        var source = $$"""
            using System.Threading.Tasks;
            namespace ManagedExecutableAsyncMainFixture;
            internal static class Program
            {
                public static async {{(returnsExitCode ? "Task<int>" : "Task")}} Main({{(hasArguments ? "string[] arguments" : "")}})
                {
                    {{(hasArguments ? "if (arguments.Length != 0) throw new System.InvalidOperationException();" : "")}}
                    await Task.Yield();
                    {{completion}}
                }
            }
            """;
        var assembly = CompileExecutable(assets, optimize, source);
        var entry = ReadEntryPoint(assembly);
        var compilation = NetWasmCompiler.Compile(new CompilerOptions(
            assembly, [assets.CoreLib], entry.TypeName, entry.MethodName, [], WasmTarget.Wasm32,
            WitPath: Path.Combine(assets.Root, "wit", "netwasm-platform-1.0.0"),
            WitWorld: "netwasm:platform/async-platform@1.0.0",
            EntryMethodToken: entry.Token,
            EntryPointKind: CompilerEntryPointKind.ManagedExecutable,
            StructuredDiagnostics: true));
        var application = Path.Combine(assets.Directory, "application.wasm");
        var runtime = Path.Combine(assets.Directory, "runtime.wasm");
        var layout = Path.Combine(assets.Directory, "runtime-layout.json");
        var component = Path.Combine(assets.Directory, "managed.component.wasm");
        File.WriteAllBytes(application, compilation.ApplicationModule);
        File.WriteAllText(layout, JsonSerializer.Serialize(new
        {
            schemaVersion = 2,
            target = "wasm32",
            applicationStaticDataEnd = compilation.StaticDataEnd,
        }));
        await RunComponentTool("bash", assets.Root,
            Path.Combine(assets.Root, "eng", "build-netwasm-runtime.sh"),
            "--runtime-layout", layout, "--output", runtime,
            "--target", "wasm32", "--configuration", "release");
        var wit = Path.Combine(assets.Directory, "wit");
        var toolchain = Path.Combine(assets.Root, "src", "NetWasm.Toolchain");
        CopyWitDirectory(Path.Combine(toolchain, "wit"), Path.Combine(wit, "deps", "wasi-cli"), recursive: false);
        CopyWitDirectory(Path.Combine(toolchain, "wit", "deps"), Path.Combine(wit, "deps"));
        CopyWitDirectory(
            Path.Combine(assets.Root, "wit", "wasi-0.2.11", "http"),
            Path.Combine(wit, "deps", "wasi-http"));
        CopyWitDirectory(Path.Combine(assets.Root, "wit", "netwasm-runtime-1.0.0"),
            Path.Combine(wit, "deps", "netwasm-runtime"));
        File.Copy(Path.Combine(toolchain, "async-wit", "world.wit"), Path.Combine(wit, "world.wit"));
        using var services = new ServiceCollection().AddNetWasmCompiler()
            .BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
        var world = services.GetRequiredService<IWitDocumentReader>().Read(wit)
            .SelectWorld("netwasm:component/async-command@1.0.0");
        var worldSpecifier = services.GetRequiredService<IWitWorldSpecifierFormatter>().Format(world);
        services.GetRequiredService<IComponentPackager>().Package(new(
            application, wit, worldSpecifier, component,
            ComponentTarget.Wasm32Wasi02, runtime,
            new(hasArguments ? ManagedExecutableParameterShape.StringArray : ManagedExecutableParameterShape.None,
                returnsExitCode ? ManagedExecutableReturnShape.ExitCode : ManagedExecutableReturnShape.Void,
                ManagedExecutableCompletionShape.Asynchronous),
            FinalWasmOptimization.None));
        var wasmTools = services.GetRequiredService<IWasmTools>();
        var validation = wasmTools.Run("validate", component, "--features", "all");
        Assert.True(validation.ExitCode == 0, validation.StandardError);
        var printed = wasmTools.Run("print", component);
        Assert.Equal(0, printed.ExitCode);
        Assert.DoesNotMatch(@"\(import\s+""(?:wasi_snapshot_preview1|wasi_unstable)""", printed.StandardOutput);
        var output = Path.Combine(assets.Directory, "jco");
        await RunComponentTool("node", assets.Root,
            Path.Combine(assets.Root, "node_modules", "@bytecodealliance", "jco", "dist", "jco.js"),
            "transpile", component, "--out-dir", output, "--name", "managed-process",
            "--instantiation", "async", "--no-wasi-shim", "--bindgen-enable-wasm-exnref");
        var observed = await RunComponentTool("node", assets.Root,
            Path.Combine(assets.Root, "tests", "end-to-end", "component-model", "run-managed-process.mjs"), output,
            expectedStatus.ToString(System.Globalization.CultureInfo.InvariantCulture),
            returnsExitCode ? "17" : "0");
        Assert.Equal("Managed component process PASS", observed.Trim());
    }

    private static void CopyWitDirectory(string source, string destination, bool recursive = true)
    {
        Directory.CreateDirectory(destination);
        foreach (var path in Directory.EnumerateFiles(source, "*.wit",
                     recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly))
        {
            var target = Path.Combine(destination, Path.GetRelativePath(source, path));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(path, target);
        }
    }

    private static async Task<string> RunComponentTool(string executable, string workingDirectory, params string[] arguments)
    {
        var start = new ProcessStartInfo(executable)
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Component tool did not start.");
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        var result = await output;
        Assert.True(process.ExitCode == 0, result + await error);
        return result;
    }

    private static string CompileExecutable(TestAssets assets, bool optimize, string source = Source)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(source);
        var compilation = CSharpCompilation.Create(
            "ManagedExecutableAsyncMainFixture",
            [syntaxTree],
            [MetadataReference.CreateFromFile(assets.CoreLib)],
            new CSharpCompilationOptions(
                OutputKind.ConsoleApplication,
                optimizationLevel: optimize
                    ? OptimizationLevel.Release
                    : OptimizationLevel.Debug));
        var path = Path.Combine(
            assets.Directory,
            "ManagedExecutableAsyncMainFixture.dll");
        var result = compilation.Emit(path);
        Assert.True(result.Success);
        return path;
    }

    private static EntryPoint ReadEntryPoint(string assemblyPath)
    {
        using var stream = File.OpenRead(assemblyPath);
        using var portableExecutable = new PEReader(stream);
        var header = portableExecutable.PEHeaders.CorHeader;
        Assert.NotNull(header);
        var handle = MetadataTokens.EntityHandle(
            header.EntryPointTokenOrRelativeVirtualAddress);
        Assert.Equal(HandleKind.MethodDefinition, handle.Kind);
        var metadata = portableExecutable.GetMetadataReader();
        var selectedMethod = metadata.GetMethodDefinition(
            (MethodDefinitionHandle)handle);
        var type = metadata.GetTypeDefinition(selectedMethod.GetDeclaringType());
        var methodHandle = type.GetMethods().Single(candidate =>
            metadata.GetString(metadata.GetMethodDefinition(candidate).Name) ==
            "Main");
        var method = metadata.GetMethodDefinition(methodHandle);
        var @namespace = metadata.GetString(type.Namespace);
        var name = metadata.GetString(type.Name);
        return new EntryPoint(
            string.IsNullOrEmpty(@namespace) ? name : $"{@namespace}.{name}",
            metadata.GetString(method.Name),
            MetadataTokens.GetToken(methodHandle));
    }

    private sealed record EntryPoint(string TypeName, string MethodName, int Token);

    private const string Source = """
        using System.Threading.Tasks;

        namespace ManagedExecutableAsyncMainFixture;

        internal static class Program
        {
            public static async Task<int> Main()
            {
                await Task.Yield();
                return 0;
            }
        }
        """;
}
