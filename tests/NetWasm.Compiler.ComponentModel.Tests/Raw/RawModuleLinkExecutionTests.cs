using NetWasm.Compiler.ComponentModel.Raw;
using NetWasm.Compiler.ComponentModel.Inlining;
using NetWasm.Compiler.ComponentModel.Optimization;

namespace NetWasm.Compiler.ComponentModel.Tests.Raw;

public sealed class RawModuleLinkExecutionTests
{
    private static readonly string[] ExpectedStages =
        ["environment", "managed-inlining", "runtime-inlining", "merge", "optimize", "publish"];

    [Fact]
    public void ResolvesOnceAndForwardsEffectiveSettingsWithoutReinterpretingPreset()
    {
        var stages = new RecordingStages();
        var resolver = new RecordingSettingsResolver();
        var options = new WasmOptimizationOverrides(ManagedInliningHints.Preserve);
        var request = new RawModuleLinkRequest("application", "runtime", "output", ComponentTarget.Wasm32Wasi02)
        { OptimizationOverrides = options };
        var execution = Assert.IsAssignableFrom<IRawModuleLinkExecution>(new RawModuleLinkExecution(
            stages, stages, stages, stages, stages, resolver, stages));
        execution.Run(request, CreateWorkspace(stages));
        Assert.Equal(1, resolver.Calls);
        Assert.Equal((FinalWasmOptimization.Oz, options), resolver.Request);
        Assert.Equal(
            ("application", Path.Combine("temporary", "sanitized.wasm"), WasmInlineAnnotationSelection.Preserve),
            stages.Inlinings[0]);
        Assert.Equal(
            ("runtime", Path.Combine("temporary", "runtime-sanitized.wasm"), WasmInlineAnnotationSelection.SizeAware),
            stages.Inlinings[1]);
        Assert.Equal(FinalWasmOptimization.O1, stages.Optimization!.Value.Item4);
    }

    [Fact]
    public void InvalidOverrideDisposesWorkspaceWithoutStartingLinkWork()
    {
        var stages = new RecordingStages();
        var request = new RawModuleLinkRequest("application", "runtime", "output", ComponentTarget.Wasm32Wasi02,
            FinalWasmOptimization.None)
        { OptimizationOverrides = new(ManagedInliningHints.Preserve) };
        Assert.Throws<ArgumentException>(() => CreateExecution(stages).Run(request, CreateWorkspace(stages)));
        Assert.Equal(["delete"], stages.Calls);
    }

    private sealed class RecordingSettingsResolver : IWasmOptimizationSettingsResolver
    {
        public int Calls { get; private set; }
        public (FinalWasmOptimization, WasmOptimizationOverrides?) Request { get; private set; }

        public WasmOptimizationSettings Resolve(FinalWasmOptimization preset, WasmOptimizationOverrides? options = null)
        {
            Calls++;
            Request = (preset, options);
            return new(
                FinalWasmOptimization.O1,
                WasmInlineAnnotationSelection.Preserve,
                WasmInlineAnnotationSelection.SizeAware);
        }
    }

    [Theory]
    [InlineData("wasm32")]
    [InlineData("wasm64")]
    public void PreservesRawExportsAndPublishesOnlyAfterOptimization(string width)
    {
        var stages = new RecordingStages();
        var request = new RawModuleLinkRequest("application", "runtime", "output", new(width, "0.2", "utf8"));

        CreateExecution(stages).Run(request, CreateWorkspace(stages));

        Assert.Equal(["environment", "managed-inlining", "runtime-inlining", "merge", "optimize", "publish", "delete"], stages.Calls);
        Assert.Equal((Path.Combine("temporary", "environment.wasm"), request.Target), stages.Environment);
        Assert.Equal(new ComponentCoreModuleMergeRequest(Path.Combine("temporary", "sanitized.wasm"),
            Path.Combine("temporary", "runtime-sanitized.wasm"),
            Path.Combine("temporary", "environment.wasm"), Path.Combine("temporary", "merged.wasm"), request.Target), stages.Merge);
        Assert.Equal((Path.Combine("temporary", "merged.wasm"), "linked", request.Target,
            FinalWasmOptimization.Oz), stages.Optimization);
        Assert.Equal(("linked", "output"), stages.Publication);
        Assert.Equal("temporary", stages.DeletedDirectory);
        Assert.Equal(
            [
                ("application", Path.Combine("temporary", "sanitized.wasm"), WasmInlineAnnotationSelection.Preserve),
                ("runtime", Path.Combine("temporary", "runtime-sanitized.wasm"), WasmInlineAnnotationSelection.SizeAware),
            ],
            stages.Inlinings);
    }

    [Theory]
    [InlineData("environment", 1)]
    [InlineData("managed-inlining", 2)]
    [InlineData("runtime-inlining", 3)]
    [InlineData("merge", 4)]
    [InlineData("optimize", 5)]
    [InlineData("publish", 6)]
    public void FailureStopsLaterWorkAndDisposesOwnedWorkspace(string stage, int count)
    {
        var stages = new RecordingStages { FailingStage = stage };

        Assert.Same(stages.Failure, Assert.Throws<InvalidOperationException>(() => CreateExecution(stages).Run(
            new("application", "runtime", "output", ComponentTarget.Wasm64Wasi02), CreateWorkspace(stages))));

        Assert.Equal(ExpectedStages.Take(count).Append("delete"), stages.Calls);
        Assert.Equal("temporary", stages.DeletedDirectory);
        Assert.Null(stages.Publication);
    }

    [Fact]
    public void RejectsMissingInputsBeforeAcquiringWorkspaceOwnership()
    {
        var stages = new RecordingStages();
        var execution = CreateExecution(stages);
        Assert.Throws<ArgumentNullException>(() => execution.Run(null!, CreateWorkspace(stages)));
        Assert.Throws<ArgumentNullException>(() => execution.Run(
            new("application", "runtime", "output", ComponentTarget.Wasm32Wasi02), null!));
        Assert.Empty(stages.Calls);
    }

    [Fact]
    public void RequiresEveryCapability()
    {
        var stages = new RecordingStages();
        Assert.Throws<ArgumentNullException>(() => new RawModuleLinkExecution(null!, stages, stages, stages, stages, new WasmOptimizationSettingsResolver(), stages));
        Assert.Throws<ArgumentNullException>(() => new RawModuleLinkExecution(stages, null!, stages, stages, stages, new WasmOptimizationSettingsResolver(), stages));
        Assert.Throws<ArgumentNullException>(() => new RawModuleLinkExecution(stages, stages, null!, stages, stages, new WasmOptimizationSettingsResolver(), stages));
        Assert.Throws<ArgumentNullException>(() => new RawModuleLinkExecution(stages, stages, stages, null!, stages, new WasmOptimizationSettingsResolver(), stages));
        Assert.Throws<ArgumentNullException>(() => new RawModuleLinkExecution(stages, stages, stages, stages, null!, new WasmOptimizationSettingsResolver(), stages));
        Assert.Throws<ArgumentNullException>(() => new RawModuleLinkExecution(stages, stages, stages, stages, stages, null!, stages));
        Assert.Throws<ArgumentNullException>(() => new RawModuleLinkExecution(stages, stages, stages, stages, stages, new WasmOptimizationSettingsResolver(), null!));
        Assert.Empty(stages.Calls);
    }

    [Fact]
    public void RejectsUnknownModeBeforeCreatingSupportModules()
    {
        var stages = new RecordingStages();
        Assert.Throws<ArgumentOutOfRangeException>(() => CreateExecution(stages).Run(
            new("application", "runtime", "output", ComponentTarget.Wasm32Wasi02,
                (FinalWasmOptimization)255), CreateWorkspace(stages)));
        Assert.Equal(["delete"], stages.Calls);
    }

    [Theory]
    [InlineData(FinalWasmOptimization.None)]
    [InlineData(FinalWasmOptimization.Oz)]
    public void RemovesOnlyPlannedInternalExportsBeforeOptimization(FinalWasmOptimization optimization)
    {
        var stages = new RecordingStages();
        var request = new RawModuleLinkRequest("application", "runtime", "output", ComponentTarget.Wasm64Wasi02, optimization)
        {
            InternalRuntimeExports = [new("native", 0), new("__heap_base", 3)],
            InternalApplicationExports = [new("callback", 0)],
        };
        CreateExecution(stages).Run(request, CreateWorkspace(stages));
        Assert.Equal(["environment", "managed-inlining", "runtime-inlining", "merge", "exports", "optimize", "publish", "delete"], stages.Calls);
        Assert.Null(stages.Selection!.ComponentPrefix);
        Assert.Equal(
            request.InternalRuntimeExports.AddRange(
                request.InternalApplicationExports),
            stages.Selection.RemovedExports);
        Assert.Equal((Path.Combine("temporary", "sanitized.wasm"), "linked", request.Target, optimization), stages.Optimization);
    }

    [Fact]
    public void ExportFailureStopsOptimizationAndPublicationAndReleasesWorkspace()
    {
        var stages = new RecordingStages { FailingStage = "exports" };
        var request = new RawModuleLinkRequest("application", "runtime", "output", ComponentTarget.Wasm32Wasi02)
        { InternalRuntimeExports = [new("native", 0)] };
        Assert.Same(stages.Failure, Assert.Throws<InvalidOperationException>(() => CreateExecution(stages).Run(request, CreateWorkspace(stages))));
        Assert.Equal(["environment", "managed-inlining", "runtime-inlining", "merge", "exports", "delete"], stages.Calls);
        Assert.Null(stages.Optimization);
        Assert.Null(stages.Publication);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RejectsUninitializedRemovalFactsBeforeCallingAnyStage(
        bool runtimeFacts)
    {
        var stages = new RecordingStages();
        var request = new RawModuleLinkRequest("application", "runtime", "output", ComponentTarget.Wasm32Wasi02)
        {
            InternalRuntimeExports = runtimeFacts ? default : [],
            InternalApplicationExports = runtimeFacts ? [] : default,
        };
        Assert.Throws<NetWasm.Compiler.Core.CompilerException>(() => CreateExecution(stages).Run(request, CreateWorkspace(stages)));
        Assert.Empty(stages.Calls);
    }

    private static IRawModuleLinkExecution CreateExecution(RecordingStages stages) =>
        Assert.IsAssignableFrom<IRawModuleLinkExecution>(new RawModuleLinkExecution(stages, stages, stages, stages, stages, new WasmOptimizationSettingsResolver(), stages));

    private static ComponentPackageWorkspace CreateWorkspace(RecordingStages stages) =>
        new(stages, "temporary", "linked", "embedded", "unstripped", "component");

    private sealed class RecordingStages : IEmscriptenEnvironmentShimWriter,
        IComponentCoreModuleMergeRunner, IComponentCoreModuleOptimizer, IFileMover, IDirectoryDeleter,
        IWasmCoreModuleExportEditor, IWasmCoreModuleInlineAnnotationEditor
    {
        public List<string> Calls { get; } = [];
        public string? FailingStage { get; init; }
        public InvalidOperationException Failure { get; } = new();
        public (string, ComponentTarget)? Environment { get; private set; }
        public ComponentCoreModuleMergeRequest? Merge { get; private set; }
        public (string, string, ComponentTarget, FinalWasmOptimization)? Optimization { get; private set; }
        public (string, string)? Publication { get; private set; }
        public string? DeletedDirectory { get; private set; }
        public WasmExportSelection? Selection { get; private set; }
        public List<(string, string, WasmInlineAnnotationSelection)> Inlinings { get; } = [];

        public void Rewrite(string inputPath, string outputPath, WasmInlineAnnotationSelection selection)
        {
            Enter(inputPath == "application" ? "managed-inlining" : "runtime-inlining");
            Inlinings.Add((inputPath, outputPath, selection));
        }
        public void Rewrite(string inputPath, string outputPath, WasmExportSelection selection)
        {
            Enter("exports");
            Assert.Equal(Path.Combine("temporary", "merged.wasm"), inputPath);
            Assert.Equal(Path.Combine("temporary", "sanitized.wasm"), outputPath);
            Selection = selection;
        }

        public void Write(string outputPath, ComponentTarget target)
        {
            Enter("environment");
            Environment = (outputPath, target);
        }

        public void Run(ComponentCoreModuleMergeRequest request)
        {
            Enter("merge");
            Merge = request;
        }

        public void Optimize(
            string inputPath,
            string outputPath,
            ComponentTarget target,
            FinalWasmOptimization optimization)
        {
            Enter("optimize");
            Optimization = (inputPath, outputPath, target, optimization);
        }

        public void Move(string sourcePath, string destinationPath)
        {
            Enter("publish");
            Publication = (sourcePath, destinationPath);
        }

        public void Delete(string path)
        {
            Calls.Add("delete");
            DeletedDirectory = path;
        }

        private void Enter(string stage)
        {
            Calls.Add(stage);
            if (stage == FailingStage) throw Failure;
        }
    }
}
