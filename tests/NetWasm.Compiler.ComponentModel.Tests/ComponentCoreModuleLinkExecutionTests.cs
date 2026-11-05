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
            stages, stages, stages, stages, stages, resolver));

        execution.Run(request, CreateWorkspace(stages));

        Assert.Equal(1, resolver.Calls);
        Assert.Equal((FinalWasmOptimization.Oz, options), resolver.Request);
        Assert.Equal(WasmInlineAnnotationSelection.Preserve, stages.Merge!.ApplicationInlineAnnotations);
        Assert.Equal(WasmInlineAnnotationSelection.SizeAware, stages.Merge.RuntimeInlineAnnotations);
        Assert.Equal(FinalWasmOptimization.O1, stages.Optimization!.Value.Item4);
        Assert.True(stages.Converge);
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
                WasmInlineAnnotationSelection.SizeAware,
                Converge: true);
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
        expected.AddRange(["merge", "optimize", "delete:environment",
            "delete:host", "delete:adapter", "delete:merged", "delete:sanitized", "delete:runtime-sanitized"]);
        Assert.Equal(expected, stages.Calls);
        Assert.Equal(("environment", target), stages.Environment);
        // The inputs merge as they are, with Oz's hint selections, keeping only the canonical ABI.
        Assert.Equal(new ComponentCoreModuleMergeRequest(
            "application", "runtime", "environment", "sanitized", target,
            managed ? "host" : null, managed ? "adapter" : null)
        {
            ApplicationInlineAnnotations = WasmInlineAnnotationSelection.Preserve,
            RuntimeInlineAnnotations = WasmInlineAnnotationSelection.SizeAware,
            Exports = stages.Merge!.Exports,
            Intermediates = new("sanitized", "runtime-sanitized", "merged"),
        }, stages.Merge);
        Assert.Equal(width == "wasm64" ? "cm64p2" : "cm32p2", stages.Merge.Exports!.ComponentPrefix);
        Assert.Empty(stages.Merge.Exports.RemovedExports);
        Assert.Equal(("sanitized", "output", target, FinalWasmOptimization.Oz),
            stages.Optimization);
        Assert.Equal(managed ? new NetWasmHostComponentShimRequest("host", target) : null, stages.Host);
        Assert.Equal(managed ? new ManagedExecutableComponentAdapterRequest("adapter", target, entry!) : null,
            stages.Adapter);
    }

    [Fact]
    public void MergeFailureReleasesWorkspaceWithoutOptimization()
    {
        var failure = new InvalidOperationException("merge failure");
        var stages = new RecordingStages { MergeFailure = failure };

        var observed = Assert.Throws<InvalidOperationException>(() => CreateExecution(stages).Run(
            new ComponentCoreModuleLinkRequest("application", "runtime", "output", ComponentTarget.Wasm32Wasi02),
            CreateWorkspace(stages)));

        Assert.Same(failure, observed);
        Assert.Equal(["environment", "merge", "delete:environment", "delete:host",
            "delete:adapter", "delete:merged", "delete:sanitized", "delete:runtime-sanitized"], stages.Calls);
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
    public void RequiresTheSettingsResolverBeforeExecution()
    {
        var stages = new RecordingStages();
        Assert.Throws<ArgumentNullException>(() => new ComponentCoreModuleLinkExecution(
            stages, stages, stages, stages, stages, null!));
        Assert.Empty(stages.Calls);
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
            new ComponentCoreModuleLinkExecution(stages, stages, stages, stages, stages,
                new WasmOptimizationSettingsResolver()));

    private static ComponentCoreModuleWorkspace CreateWorkspace(RecordingStages stages) =>
        new(stages, "environment", "host", "adapter", "merged", "sanitized", "runtime-sanitized");

    private sealed class RecordingStages : IComponentCoreModuleMergeRunner,
        IEmscriptenEnvironmentShimWriter, INetWasmHostComponentShimWriter,
        IManagedExecutableComponentAdapterWriter, IComponentCoreModuleOptimizer, IFileDeleter
    {
        public List<string> Calls { get; } = [];
        public Exception? MergeFailure { get; init; }
        public (string, ComponentTarget)? Environment { get; private set; }
        public ComponentCoreModuleMergeRequest? Merge { get; private set; }
        public NetWasmHostComponentShimRequest? Host { get; private set; }
        public ManagedExecutableComponentAdapterRequest? Adapter { get; private set; }
        public (string, string, ComponentTarget, FinalWasmOptimization)? Optimization { get; private set; }
        public bool? Converge { get; private set; }

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

        public void Optimize(
            string inputPath,
            string outputPath,
            ComponentTarget target,
            FinalWasmOptimization optimization,
            bool converge)
        {
            Calls.Add("optimize");
            Optimization = (inputPath, outputPath, target, optimization);
            Converge = converge;
        }

        public void Delete(string path) => Calls.Add("delete:" + path);
    }
}
