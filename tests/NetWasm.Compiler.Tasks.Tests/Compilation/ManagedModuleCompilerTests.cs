using System.Collections.Immutable;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Core.ManagedExecutables;
using NetWasm.Compiler.Tasks.Compilation;
using NetWasm.Compiler.Tasks.Tests.TestSupport;

namespace NetWasm.Compiler.Tasks.Tests.Compilation;

public sealed class ManagedModuleCompilerTests
{
    [Fact]
    public void CompileTranslatesTheManagedModuleRequest()
    {
        var entryPoints = new RecordingEntryPointReader(
            new ManagedEntryPoint(
                "Example.Program",
                "Main",
                0x06000001,
                new(
                    ManagedExecutableParameterShape.StringArray,
                    ManagedExecutableReturnShape.ExitCode)));
        var targets = new RecordingTargetResolver(WasmTarget.Wasm64);
        var invoker = new RecordingCompilationInvoker(
            CompilerTaskTestData.CreateCompilation("wasm64"));
        var compiler = Assert.IsAssignableFrom<IManagedModuleCompiler>(
            new ManagedModuleCompiler(entryPoints, targets, invoker));
        var request = new ManagedModuleCompileRequest(
            "application.dll",
            ["library.dll", "corelib.dll"],
            ["Program.cs"],
            "wasm64",
            "trace.jsonl",
            "compiler.log",
            true,
            "None",
            "obj/netwasm",
            "compiler-wit",
            "netwasm:platform@1.0.0/platform",
            UseJavaScriptExportBoundary: true,
            ProjectDirectory: "/project",
            PathMap: "/source=/mapped",
            StructuredDiagnostics: true);

        var result = compiler.Compile(request);

        Assert.Equal(invoker.Result with
        {
            EntryPoint = entryPoints.Result.Abi,
        }, result);
        Assert.Equal(entryPoints.Result.Abi, result.EntryPoint);
        Assert.Equal("application.dll", entryPoints.AssemblyPath);
        Assert.Equal("wasm64", targets.Target);
        var options = Assert.IsType<CompilerOptions>(invoker.Options);
        Assert.Equal(request.InputAssemblyPath, options.EntryAssemblyPath);
        Assert.Equal(request.ReferencePaths, options.ReferencePaths);
        Assert.Equal("Example.Program", options.EntryTypeName);
        Assert.Equal("Main", options.EntryMethodName);
        Assert.Empty(options.Exports);
        Assert.Equal(WasmTarget.Wasm64, options.Target);
        Assert.Equal(request.DiagnosticTracePath, options.DiagnosticTracePath);
        Assert.Equal(request.DiagnosticLogPath, options.DiagnosticLogPath);
        Assert.Equal(request.SourcePaths, options.SourcePaths);
        Assert.Equal(request.WitPath, options.WitPath);
        Assert.Equal(request.WitWorld, options.WitWorld);
        Assert.True(options.EnableFrontendCache);
        Assert.Equal(request.IntermediateOutputPath, options.IntermediateOutputPath);
        Assert.True(options.UseJavaScriptExportBoundary);
        Assert.Equal(request.ProjectDirectory, options.ProjectDirectory);
        Assert.Equal(request.PathMap, options.PathMap);
        Assert.True(options.EmitStackTrace);
        Assert.True(options.StructuredDiagnostics);
        Assert.Equal(0x06000001, options.EntryMethodToken);
        Assert.Equal(CompilerEntryPointKind.ManagedExecutable, options.EntryPointKind);
    }

    [Theory]
    [InlineData("wasm32")]
    [InlineData("wasm64")]
    public void LibraryCompilationDoesNotReadOrFabricateAnEntryPoint(string target)
    {
        var entryPoints = new RecordingEntryPointReader(CreateEntryPoint());
        var width = target == "wasm32" ? WasmTarget.Wasm32 : WasmTarget.Wasm64;
        var targets = new RecordingTargetResolver(width);
        var invoker = new RecordingCompilationInvoker(
            CompilerTaskTestData.CreateCompilation(target) with
            {
                EntryPoint = CreateEntryPoint().Abi,
            });
        var compiler = Assert.IsAssignableFrom<IManagedModuleCompiler>(
            new ManagedModuleCompiler(entryPoints, targets, invoker));
        var request = new ManagedModuleCompileRequest(
            "worker.dll", ["corelib.dll"], ["Worker.cs"], target,
            null, null, false, "None", "obj/netwasm", "worker.wit", "worker",
            CompileAsLibrary: true);

        var result = compiler.Compile(request);

        Assert.Null(entryPoints.AssemblyPath);
        Assert.Null(result.EntryPoint);
        var options = Assert.IsType<CompilerOptions>(invoker.Options);
        Assert.Equal(CompilerEntryPointKind.Library, options.EntryPointKind);
        Assert.Empty(options.EntryTypeName);
        Assert.Empty(options.EntryMethodName);
        Assert.Null(options.EntryMethodToken);
        Assert.Equal(request.WitPath, options.WitPath);
        Assert.Equal(request.WitWorld, options.WitWorld);
        Assert.Equal(request.ReferencePaths, options.ReferencePaths);
        Assert.Equal(request.SourcePaths, options.SourcePaths);
        Assert.Equal(width, options.Target);
        Assert.True(options.EnableFrontendCache);
        Assert.Equal(request.IntermediateOutputPath, options.IntermediateOutputPath);
        Assert.Equal(invoker.Result with { EntryPoint = null }, result);
    }

    [Theory]
    [InlineData("None")]
    [InlineData("O0")]
    [InlineData("O1")]
    [InlineData("O2")]
    [InlineData("O3")]
    [InlineData("Os")]
    [InlineData("Oz")]
    public void CompileEnablesFrontendCacheForEveryCanonicalOptimizationMode(
        string optimization)
    {
        var invoker = new RecordingCompilationInvoker(
            CompilerTaskTestData.CreateCompilation());
        var compiler = new ManagedModuleCompiler(
            new RecordingEntryPointReader(CreateEntryPoint()),
            new RecordingTargetResolver(WasmTarget.Wasm32), invoker);

        compiler.Compile(new("application.dll", [], [], "wasm32", null, null,
            false, optimization, "obj/netwasm"));

        Assert.True(invoker.Options!.EnableFrontendCache);
    }

    [Fact]
    public void CompileRejectsANullRequest()
    {
        var compiler = CreateCompiler();

        Assert.Throws<ArgumentNullException>(() => compiler.Compile(null!));
    }

    [Fact]
    public void ConstructorRejectsMissingCapabilities()
    {
        var entryPoints = new RecordingEntryPointReader(CreateEntryPoint());
        var targets = new RecordingTargetResolver(WasmTarget.Wasm32);
        var invoker = new RecordingCompilationInvoker(
            CompilerTaskTestData.CreateCompilation());

        Assert.Throws<ArgumentNullException>(() =>
            new ManagedModuleCompiler(null!, targets, invoker));
        Assert.Throws<ArgumentNullException>(() =>
            new ManagedModuleCompiler(entryPoints, null!, invoker));
        Assert.Throws<ArgumentNullException>(() =>
            new ManagedModuleCompiler(entryPoints, targets, null!));
    }

    private static ManagedModuleCompiler CreateCompiler() =>
        new ManagedModuleCompiler(
            new RecordingEntryPointReader(CreateEntryPoint()),
            new RecordingTargetResolver(WasmTarget.Wasm32),
            new RecordingCompilationInvoker(CompilerTaskTestData.CreateCompilation()));

    private static ManagedEntryPoint CreateEntryPoint() => new(
        "Program",
        "Main",
        1,
        new(ManagedExecutableParameterShape.None, ManagedExecutableReturnShape.Void));

    private sealed class RecordingEntryPointReader(ManagedEntryPoint result) :
        IManagedEntryPointReader
    {
        public string? AssemblyPath { get; private set; }
        public ManagedEntryPoint Result => result;

        public ManagedEntryPoint Read(string assemblyPath)
        {
            AssemblyPath = assemblyPath;
            return result;
        }
    }

    private sealed class RecordingTargetResolver(WasmTarget result) : IWasmTargetResolver
    {
        public string? Target { get; private set; }

        public WasmTarget Resolve(string target)
        {
            Target = target;
            return result;
        }
    }

    private sealed class RecordingCompilationInvoker(ManagedModuleCompilation result) :
        INetWasmCompilationInvoker
    {
        public CompilerOptions? Options { get; private set; }
        public ManagedModuleCompilation Result => result;

        public ManagedModuleCompilation Compile(CompilerOptions options)
        {
            Options = options;
            return result;
        }
    }
}
