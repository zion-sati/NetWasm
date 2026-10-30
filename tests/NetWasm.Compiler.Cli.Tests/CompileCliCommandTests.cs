using NetWasm.Compiler.Cli.Tests.TestSupport;
using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.ComponentModel.Optimization;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Core.ManagedExecutables;

namespace NetWasm.Compiler.Cli.Tests;

public sealed class CompileCliCommandTests
{
    private static readonly ManagedExecutableEntryPointAbi AsyncMain = new(
        ManagedExecutableParameterShape.None,
        ManagedExecutableReturnShape.ExitCode,
        ManagedExecutableCompletionShape.Asynchronous);

    [Fact]
    public void CompilesAnExecutableFromTheAssemblyEntryPoint()
    {
        var abi = new ManagedExecutableEntryPointAbi(
            ManagedExecutableParameterShape.StringArray, ManagedExecutableReturnShape.Void);
        var harness = new Harness(new ManagedEntryPoint("Example.Program", "Main", 0x06000007, abi));

        var result = harness.Run("App.dll", "-out:bin/app.wasm");

        Assert.Equal(0, result);
        Assert.Equal(["App.dll"], harness.EntryPoints.Reads);
        var options = harness.Sessions.Options!;
        Assert.Equal("App.dll", options.EntryAssemblyPath);
        Assert.Equal("Example.Program", options.EntryTypeName);
        Assert.Equal("Main", options.EntryMethodName);
        Assert.Equal(0x06000007, options.EntryMethodToken);
        Assert.Equal(CompilerEntryPointKind.ManagedExecutable, options.EntryPointKind);
        Assert.True(harness.Sessions.Disposed);
        var request = harness.Artifacts.Request!;
        Assert.Equal("bin/app.wasm", request.CoreModulePath);
        Assert.Equal(Path.Combine("bin", "app.callbacks.o"), request.CallbackObjectPath);
        Assert.Equal(abi, request.Compilation.EntryPoint);
        Assert.Null(request.RuntimeLayoutPath);
        Assert.Null(request.InteropManifestPath);
        Assert.Null(request.CompilerMetadataPath);
        Assert.Null(request.ExceptionTypeMapPath);
        Assert.Null(request.StackTraceSymbolsPath);
    }

    [Theory]
    [InlineData(true, ManagedExecutableCompletionShape.Synchronous)]
    [InlineData(false, ManagedExecutableCompletionShape.Asynchronous)]
    public void RecordsHowTheCompiledEntryOfAnAsyncMainCompletes(
        bool synchronousEntry, ManagedExecutableCompletionShape expected)
    {
        var harness = new Harness(
            new ManagedEntryPoint("Example.Program", "Main", 1, AsyncMain),
            CliTestData.Compilation() with { SynchronousEntry = synchronousEntry });

        harness.Run("App.dll", "-out:app.wasm");

        Assert.Equal(AsyncMain with { CompletionShape = expected }, harness.Artifacts.Request!.Compilation.EntryPoint);
    }

    [Fact]
    public void CompilesAFunctionModuleFromItsEntryMethod()
    {
        var harness = new Harness(entryPoint: null);

        harness.Run("App.dll", "-out:app.wasm", "-target:function", "-entry:Example.Entry::Run");

        Assert.Empty(harness.EntryPoints.Reads);
        var options = harness.Sessions.Options!;
        Assert.Equal("Example.Entry", options.EntryTypeName);
        Assert.Equal("Run", options.EntryMethodName);
        Assert.Null(options.EntryMethodToken);
        Assert.Equal(CompilerEntryPointKind.RawFunction, options.EntryPointKind);
        Assert.Null(harness.Artifacts.Request!.Compilation.EntryPoint);
    }

    [Fact]
    public void CompilesALibraryWithoutAnEntry()
    {
        var harness = new Harness(entryPoint: null);

        harness.Run("Worker.dll", "-out:worker.wasm", "-target:library");

        Assert.Empty(harness.EntryPoints.Reads);
        var options = harness.Sessions.Options!;
        Assert.Equal(CompilerEntryPointKind.Library, options.EntryPointKind);
        Assert.Empty(options.EntryTypeName);
        Assert.Empty(options.EntryMethodName);
    }

    [Fact]
    public void PassesEveryOptionToTheCompilerAndTheArtifactWriter()
    {
        var harness = new Harness(entryPoint: null);

        harness.Run(
            "App.dll", "-out:app.wasm", "-target:function", "-entry:Example.Entry::Run", "-r:CoreLib.dll",
            "-export:add=Example.Math::Add", "-platform:wasm64", "-optimize:O3", "-pathmap:/src=/_/",
            "-source:Entry.cs", "-interopmanifest:interop.json", "-runtimelayout:layout.json",
            "-compilermetadata:metadata.json", "-exceptiontypemap:exceptions.json",
            "-stacktracesymbols:symbols.json", "-callbackobject:native/callbacks.o", "-wit:contract.wit",
            "-world:example:test/test", "-importmodule:host_ui", "-diagnostictrace:trace.json",
            "-diagnosticlog:compiler.jsonl", "-jsexportboundary+", "-structureddiagnostics+",
            "-projectdirectory:/project", "-intermediatedirectory:/project/obj", "-wasmtoolsnode:node",
            "-wasmtoolscommand:run.mjs", "-wasmtoolsmodule:wasm-tools.wasm",
            "-managedloopinvariantfieldhoisting:Disabled", "-managedstaticdataviewelimination:Enabled",
            "-managedaddresstakenvaluepromotion:Disabled", "-managedmethodinlining:Disabled",
            "-managedbranchprefixduplication:Disabled", "-managedscalarcallpreservation:Enabled",
            "-managedarrayboundscheckelimination:Disabled", "-managedloopunrolling:Enabled",
            "-managedconstantargumentpropagation:Enabled", "-managedstaticarraypopulationelimination:Disabled",
            "-managedconstantcallfolding:Disabled", "-managedswitchbittests:Enabled",
            "-manageddevirtualization:Disabled", "-managedallocationelimination:Enabled",
            "-managedlooprotation:Enabled", "-managedarraystorecheckelimination:Disabled",
            "-manageddispatcherelimination:Disabled",
            "-managedemptyfinallyremoval:Enabled",
            "-managedstructscalarreplacement:Disabled",
            "-managedallocationscalarreplacement:Enabled",
            "-managedidempotentloopcollapse:Disabled",
            "-managedloopstoreforwarding:Enabled",
            "-managedarrayloopidioms:Disabled",
            "-managedloopvectorization:Disabled");

        Assert.Equal("node", harness.Sessions.WasmTools!.Executable);
        var options = harness.Sessions.Options!;
        Assert.Equal(["CoreLib.dll"], options.ReferencePaths);
        Assert.Equal(new RequestedExport("add", "Example.Math", "Add"), Assert.Single(options.Exports));
        Assert.Equal(WasmTarget.Wasm64, options.Target);
        Assert.Equal(FinalWasmOptimization.O3, options.Optimization);
        Assert.Equal("/src=/_/", options.PathMap);
        Assert.Equal(["Entry.cs"], options.SourcePaths);
        Assert.True(options.EmitStackTrace);
        Assert.Equal("contract.wit", options.WitPath);
        Assert.Equal("example:test/test", options.WitWorld);
        Assert.Equal(["host_ui"], options.ImportModules);
        Assert.Equal("trace.json", options.DiagnosticTracePath);
        Assert.Equal("compiler.jsonl", options.DiagnosticLogPath);
        Assert.True(options.UseJavaScriptExportBoundary);
        Assert.True(options.StructuredDiagnostics);
        Assert.Equal("/project", options.ProjectDirectory);
        Assert.True(options.EnableFrontendCache);
        Assert.Equal("/project/obj", options.IntermediateOutputPath);
        var overrides = options.OptimizationOverrides!;
        Assert.Equal(ManagedLoopInvariantFieldHoisting.Disabled, overrides.LoopInvariantFieldHoisting);
        Assert.Equal(ManagedStaticDataViewElimination.Enabled, overrides.StaticDataViewElimination);
        Assert.Equal(ManagedBranchPrefixDuplication.Disabled, overrides.BranchPrefixDuplication);
        Assert.Equal(ManagedScalarCallPreservation.Enabled, overrides.ScalarCallPreservation);
        Assert.Equal(ManagedArrayBoundsCheckElimination.Disabled, overrides.ArrayBoundsCheckElimination);
        Assert.Equal(ManagedLoopUnrolling.Enabled, overrides.LoopUnrolling);
        Assert.Equal(ManagedConstantArgumentPropagation.Enabled, overrides.ConstantArgumentPropagation);
        Assert.Equal(ManagedStaticArrayPopulationElimination.Disabled, overrides.StaticArrayPopulationElimination);
        Assert.Equal(ManagedConstantCallFolding.Disabled, overrides.ConstantCallFolding);
        Assert.Equal(ManagedSwitchBitTests.Enabled, overrides.SwitchBitTests);
        Assert.Equal(ManagedDevirtualization.Disabled, overrides.Devirtualization);
        Assert.Equal(ManagedAllocationElimination.Enabled, overrides.AllocationElimination);
        Assert.Equal(ManagedLoopRotation.Enabled, overrides.LoopRotation);
        Assert.Equal(ManagedArrayStoreCheckElimination.Disabled, overrides.ArrayStoreCheckElimination);
        Assert.Equal(ManagedDispatcherElimination.Disabled, overrides.DispatcherElimination);
        Assert.Equal(ManagedEmptyFinallyRemoval.Enabled, overrides.EmptyFinallyRemoval);
        Assert.Equal(ManagedStructScalarReplacement.Disabled, overrides.StructScalarReplacement);
        Assert.Equal(ManagedAllocationScalarReplacement.Enabled, overrides.AllocationScalarReplacement);
        Assert.Equal(ManagedIdempotentLoopCollapse.Disabled, overrides.IdempotentLoopCollapse);
        Assert.Equal(ManagedLoopStoreForwarding.Enabled, overrides.LoopStoreForwarding);
        Assert.Equal(ManagedArrayLoopIdioms.Disabled, overrides.ArrayLoopIdioms);
        Assert.Equal(ManagedLoopVectorization.Disabled, overrides.LoopVectorization);
        var request = harness.Artifacts.Request!;
        Assert.Equal("native/callbacks.o", request.CallbackObjectPath);
        Assert.Equal("layout.json", request.RuntimeLayoutPath);
        Assert.Equal("interop.json", request.InteropManifestPath);
        Assert.Equal("metadata.json", request.CompilerMetadataPath);
        Assert.Equal("exceptions.json", request.ExceptionTypeMapPath);
        Assert.Equal("symbols.json", request.StackTraceSymbolsPath);
    }

    [Fact]
    public void LeavesTheFrontendCacheAndWasmToolsUnsetByDefault()
    {
        var harness = new Harness(entryPoint: null);

        harness.Run("Worker.dll", "-out:worker.wasm", "-target:library");

        Assert.Null(harness.Sessions.WasmTools);
        var options = harness.Sessions.Options!;
        Assert.False(options.EnableFrontendCache);
        Assert.Null(options.IntermediateOutputPath);
        Assert.False(options.EmitStackTrace);
        Assert.Equal(FinalWasmOptimization.Oz, options.Optimization);
    }

    [Fact]
    public void ReportsTheCommandNameAndOptions()
    {
        var command = new Harness(entryPoint: null).Command;

        Assert.Empty(command.Name);
        Assert.Same(CompileCliOptions.Names, command.Options);
    }

    [Fact]
    public void RejectsMissingInputs()
    {
        var sessions = new RecordingSessionFactory(CliTestData.Compilation());
        var entryPoints = new RecordingEntryPointReader(null);
        var artifacts = new RecordingArtifactWriter();
        var overrides = new ManagedCompilerOptimizationOverridesParser();

        Assert.Throws<ArgumentNullException>(() => new CompileCliCommand(null!, entryPoints, artifacts, overrides));
        Assert.Throws<ArgumentNullException>(() => new CompileCliCommand(sessions, null!, artifacts, overrides));
        Assert.Throws<ArgumentNullException>(() => new CompileCliCommand(sessions, entryPoints, null!, overrides));
        Assert.Throws<ArgumentNullException>(() => new CompileCliCommand(sessions, entryPoints, artifacts, null!));
    }

    private sealed class Harness
    {
        public Harness(ManagedEntryPoint? entryPoint, ManagedModuleCompilation? compilation = null)
        {
            EntryPoints = new RecordingEntryPointReader(entryPoint);
            Sessions = new RecordingSessionFactory(compilation ?? CliTestData.Compilation());
            Command = new CompileCliCommand(
                Sessions, EntryPoints, Artifacts, new ManagedCompilerOptimizationOverridesParser());
        }

        public RecordingEntryPointReader EntryPoints { get; }
        public RecordingSessionFactory Sessions { get; }
        public RecordingArtifactWriter Artifacts { get; } = new();
        public CompileCliCommand Command { get; }

        public int Run(params string[] arguments) =>
            Command.Run(NwcTestArguments.Read(CompileCliOptions.Names, arguments));
    }

    private sealed class RecordingEntryPointReader(ManagedEntryPoint? entryPoint) : IManagedEntryPointReader
    {
        public List<string> Reads { get; } = [];

        public ManagedEntryPoint Read(string assemblyPath)
        {
            Reads.Add(assemblyPath);
            return entryPoint!;
        }
    }

    private sealed class RecordingSessionFactory(ManagedModuleCompilation compilation) :
        INwcCompilerSessionFactory, INwcCompilerSession
    {
        public ExternalToolCommand? WasmTools { get; private set; }
        public CompilerOptions? Options { get; private set; }
        public bool Disposed { get; private set; }

        public INwcCompilerSession Create(ExternalToolCommand? wasmTools)
        {
            WasmTools = wasmTools;
            return this;
        }

        public ManagedModuleCompilation Compile(CompilerOptions options)
        {
            Options = options;
            return compilation;
        }

        public void Dispose() => Disposed = true;
    }

    private sealed class RecordingArtifactWriter : ICompilerArtifactWriter
    {
        public CompilerArtifactWriteRequest? Request { get; private set; }

        public void Write(CompilerArtifactWriteRequest request) => Request = request;
    }
}
