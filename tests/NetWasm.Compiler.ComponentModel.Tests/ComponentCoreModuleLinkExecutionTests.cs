using NetWasm.Compiler.ComponentModel.ManagedExecutables;
using NetWasm.Compiler.ComponentModel.Inlining;
using NetWasm.Compiler.ComponentModel.Optimization;
using NetWasm.Compiler.Core.ManagedExecutables;

namespace NetWasm.Compiler.ComponentModel.Tests;

public sealed class ComponentCoreModuleLinkExecutionTests
{
    [Fact]
    public void ResolvesOnceAndForwardsEffectiveSettingsWithoutReinterpretingPreset()
    {
        var stages = new RecordingStages();
        var resolver = new RecordingSettingsResolver();
        var options = new WasmOptimizationOverrides(ManagedInliningHints.Preserve);
        var request = new ComponentCoreModuleLinkRequest("application", "runtime", "output", ComponentTarget.Wasm32Wasi02)
        { OptimizationOverrides = options };
        var execution = Assert.IsAssignableFrom<IComponentCoreModuleLinkExecution>(new ComponentCoreModuleLinkExecution(
            stages, stages, stages, stages, stages, stages, resolver, stages));

        execution.Run(request, CreateWorkspace(stages));

        Assert.Equal(1, resolver.Calls);
        Assert.Equal((FinalWasmOptimization.Oz, options), resolver.Request);
        Assert.Equal(
            ("application", "sanitized", WasmInlineAnnotationSelection.Preserve),
            stages.Inlinings[0]);
        Assert.Equal(
            ("runtime", "runtime-sanitized", WasmInlineAnnotationSelection.SizeAware),
            stages.Inlinings[1]);
        Assert.Equal(FinalWasmOptimization.O1, stages.Optimization!.Value.Item4);
    }

    [Fact]
    public void InvalidOverrideDisposesWorkspaceWithoutStartingLinkWork()
    {
        var stages = new RecordingStages();
        var request = new ComponentCoreModuleLinkRequest("application", "runtime", "output", ComponentTarget.Wasm32Wasi02,
            Optimization: FinalWasmOptimization.None)
        { OptimizationOverrides = new(ManagedInliningHints.Preserve) };
        Assert.Throws<ArgumentException>(() => CreateExecution(stages).Run(request, CreateWorkspace(stages)));
        Assert.Equal(["delete:environment", "delete:host", "delete:adapter", "delete:merged", "delete:sanitized",
            "delete:runtime-sanitized"], stages.Calls);
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
    [InlineData("wasm32", false)]
    [InlineData("wasm64", false)]
    [InlineData("wasm32", true)]
    [InlineData("wasm64", true)]
    public void LinksThroughCapabilitiesAndReleasesOnlyOwnedModules(string width, bool managed)
    {
        var stages = new RecordingStages();
        var target = width == "wasm64" ? ComponentTarget.Wasm64Wasi02 : ComponentTarget.Wasm32Wasi02;
        var entry = managed ? new ManagedExecutableEntryPointAbi(
            ManagedExecutableParameterShape.StringArray, ManagedExecutableReturnShape.ExitCode) : null;
        var request = new ComponentCoreModuleLinkRequest("application", "runtime", "output", target, entry);

        CreateExecution(stages).Run(request, CreateWorkspace(stages));

        var expected = new List<string> { "environment" };
        if (managed) expected.AddRange(["host", "adapter"]);
        expected.AddRange(["inlining", "inlining", "merge", "exports", "optimize", "delete:environment",
            "delete:host", "delete:adapter", "delete:merged", "delete:sanitized", "delete:runtime-sanitized"]);
        Assert.Equal(expected, stages.Calls);
        Assert.Equal(("environment", target), stages.Environment);
        Assert.Equal(new ComponentCoreModuleMergeRequest(
            "sanitized", "runtime-sanitized", "environment", "merged", target,
            managed ? "host" : null, managed ? "adapter" : null), stages.Merge);
        Assert.Equal(("merged", "sanitized", width == "wasm64" ? "cm64p2" : "cm32p2"), stages.Exports);
        Assert.Equal(("sanitized", "output", target, FinalWasmOptimization.Oz),
            stages.Optimization);
        Assert.Equal(
            [
                ("application", "sanitized", WasmInlineAnnotationSelection.Preserve),
                ("runtime", "runtime-sanitized", WasmInlineAnnotationSelection.SizeAware),
            ],
            stages.Inlinings);
        Assert.Equal(managed ? new NetWasmHostComponentShimRequest("host", target) : null, stages.Host);
        Assert.Equal(managed ? new ManagedExecutableComponentAdapterRequest("adapter", target, entry!) : null,
            stages.Adapter);
    }

    [Fact]
    public void MergeFailureReleasesWorkspaceWithoutExportFilteringOrOptimization()
    {
        var failure = new InvalidOperationException("merge failure");
        var stages = new RecordingStages { MergeFailure = failure };

        var observed = Assert.Throws<InvalidOperationException>(() => CreateExecution(stages).Run(
            new ComponentCoreModuleLinkRequest("application", "runtime", "output", ComponentTarget.Wasm32Wasi02),
            CreateWorkspace(stages)));

        Assert.Same(failure, observed);
        Assert.Equal(["environment", "inlining", "inlining", "merge", "delete:environment", "delete:host",
            "delete:adapter", "delete:merged", "delete:sanitized", "delete:runtime-sanitized"], stages.Calls);
        Assert.Null(stages.Exports);
        Assert.Null(stages.Optimization);
    }

    [Fact]
    public void RejectsMissingInputsBeforeInvokingCapabilities()
    {
        var stages = new RecordingStages();
        var execution = CreateExecution(stages);
        Assert.Throws<ArgumentNullException>(() => execution.Run(null!, CreateWorkspace(stages)));
        Assert.Throws<ArgumentNullException>(() => execution.Run(
            new ComponentCoreModuleLinkRequest("application", "runtime", "output", ComponentTarget.Wasm32Wasi02),
            null!));
        Assert.Empty(stages.Calls);
    }

    [Fact]
    public void RequiresInliningCapabilitiesBeforeExecution()
    {
        var stages = new RecordingStages();
        Assert.Throws<ArgumentNullException>(() => new ComponentCoreModuleLinkExecution(
            stages, stages, stages, stages, stages, stages, null!, stages));
        Assert.Throws<ArgumentNullException>(() => new ComponentCoreModuleLinkExecution(
            stages, stages, stages, stages, stages, stages, new WasmOptimizationSettingsResolver(), null!));
        Assert.Empty(stages.Calls);
    }

    [Fact]
    public void SelectionFailureReleasesWorkspaceWithoutMerging()
    {
        var failure = new InvalidOperationException("annotation failure");
        var stages = new RecordingStages { InliningFailure = failure };
        Assert.Same(failure, Assert.Throws<InvalidOperationException>(() => CreateExecution(stages).Run(
            new("application", "runtime", "output", ComponentTarget.Wasm32Wasi02), CreateWorkspace(stages))));
        Assert.Equal(["environment", "inlining", "delete:environment", "delete:host",
            "delete:adapter", "delete:merged", "delete:sanitized", "delete:runtime-sanitized"], stages.Calls);
        Assert.Null(stages.Merge);
        Assert.Null(stages.Optimization);
    }

    [Fact]
    public void RejectsUnknownModeBeforeCreatingSupportModules()
    {
        var stages = new RecordingStages();
        Assert.Throws<ArgumentOutOfRangeException>(() => CreateExecution(stages).Run(
            new("application", "runtime", "output", ComponentTarget.Wasm32Wasi02,
                Optimization: (FinalWasmOptimization)255), CreateWorkspace(stages)));
        Assert.Equal(["delete:environment", "delete:host", "delete:adapter", "delete:merged", "delete:sanitized",
            "delete:runtime-sanitized"], stages.Calls);
    }

    private static IComponentCoreModuleLinkExecution CreateExecution(RecordingStages stages) =>
        Assert.IsAssignableFrom<IComponentCoreModuleLinkExecution>(
            new ComponentCoreModuleLinkExecution(stages, stages, stages, stages, stages, stages,
                new WasmOptimizationSettingsResolver(), stages));

    private static ComponentCoreModuleWorkspace CreateWorkspace(RecordingStages stages) =>
        new(stages, "environment", "host", "adapter", "merged", "sanitized", "runtime-sanitized");

    private sealed class RecordingStages : IComponentCoreModuleMergeRunner,
        IEmscriptenEnvironmentShimWriter, INetWasmHostComponentShimWriter,
        IManagedExecutableComponentAdapterWriter, IWasmCoreModuleExportEditor,
        IComponentCoreModuleOptimizer, IFileDeleter, IWasmCoreModuleInlineAnnotationEditor
    {
        public List<string> Calls { get; } = [];
        public Exception? MergeFailure { get; init; }
        public Exception? InliningFailure { get; init; }
        public (string, ComponentTarget)? Environment { get; private set; }
        public ComponentCoreModuleMergeRequest? Merge { get; private set; }
        public NetWasmHostComponentShimRequest? Host { get; private set; }
        public ManagedExecutableComponentAdapterRequest? Adapter { get; private set; }
        public (string, string, string)? Exports { get; private set; }
        public (string, string, ComponentTarget, FinalWasmOptimization)? Optimization { get; private set; }
        public List<(string, string, WasmInlineAnnotationSelection)> Inlinings { get; } = [];

        public void Rewrite(string inputPath, string outputPath, WasmInlineAnnotationSelection selection)
        {
            Calls.Add("inlining");
            if (InliningFailure is not null) throw InliningFailure;
            Inlinings.Add((inputPath, outputPath, selection));
        }

        public void Write(string outputPath, ComponentTarget target)
        {
            Calls.Add("environment");
            Environment = (outputPath, target);
        }

        public void Write(NetWasmHostComponentShimRequest request)
        {
            Calls.Add("host");
            Host = request;
        }

        public void Write(ManagedExecutableComponentAdapterRequest request)
        {
            Calls.Add("adapter");
            Adapter = request;
        }

        public void Run(ComponentCoreModuleMergeRequest request)
        {
            Calls.Add("merge");
            Merge = request;
            if (MergeFailure is not null) throw MergeFailure;
        }

        public void Rewrite(string inputPath, string outputPath, WasmExportSelection selection)
        {
            Calls.Add("exports");
            Exports = (inputPath, outputPath, selection.ComponentPrefix!);
        }

        public void Optimize(
            string inputPath,
            string outputPath,
            ComponentTarget target,
            FinalWasmOptimization optimization)
        {
            Calls.Add("optimize");
            Optimization = (inputPath, outputPath, target, optimization);
        }

        public void Delete(string path) => Calls.Add("delete:" + path);
    }
}
