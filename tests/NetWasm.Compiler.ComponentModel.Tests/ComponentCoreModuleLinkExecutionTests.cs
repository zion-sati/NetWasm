using NetWasm.Compiler.ComponentModel.ManagedExecutables;
using NetWasm.Compiler.Core.ManagedExecutables;

namespace NetWasm.Compiler.ComponentModel.Tests;

public sealed class ComponentCoreModuleLinkExecutionTests
{
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
        expected.AddRange(["merge", "exports", "optimize", "delete:environment",
            "delete:host", "delete:adapter", "delete:merged", "delete:sanitized"]);
        Assert.Equal(expected, stages.Calls);
        Assert.Equal(("environment", target), stages.Environment);
        Assert.Equal(new ComponentCoreModuleMergeRequest(
            "application", "runtime", "environment", "merged", target,
            managed ? "host" : null, managed ? "adapter" : null), stages.Merge);
        Assert.Equal(("merged", "sanitized", width == "wasm64" ? "cm64p2" : "cm32p2"), stages.Exports);
        Assert.Equal(("sanitized", "output", target), stages.Optimization);
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
        Assert.Equal(["environment", "merge", "delete:environment", "delete:host",
            "delete:adapter", "delete:merged", "delete:sanitized"], stages.Calls);
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

    private static IComponentCoreModuleLinkExecution CreateExecution(RecordingStages stages) =>
        Assert.IsAssignableFrom<IComponentCoreModuleLinkExecution>(
            new ComponentCoreModuleLinkExecution(stages, stages, stages, stages, stages, stages));

    private static ComponentCoreModuleWorkspace CreateWorkspace(RecordingStages stages) =>
        new(stages, "environment", "host", "adapter", "merged", "sanitized");

    private sealed class RecordingStages : IComponentCoreModuleMergeRunner,
        IEmscriptenEnvironmentShimWriter, INetWasmHostComponentShimWriter,
        IManagedExecutableComponentAdapterWriter, IWasmCoreModuleExportEditor,
        IComponentCoreModuleOptimizer, IFileDeleter
    {
        public List<string> Calls { get; } = [];
        public Exception? MergeFailure { get; init; }
        public (string, ComponentTarget)? Environment { get; private set; }
        public ComponentCoreModuleMergeRequest? Merge { get; private set; }
        public NetWasmHostComponentShimRequest? Host { get; private set; }
        public ManagedExecutableComponentAdapterRequest? Adapter { get; private set; }
        public (string, string, string)? Exports { get; private set; }
        public (string, string, ComponentTarget)? Optimization { get; private set; }

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

        public void RetainComponentExports(string inputPath, string outputPath, string prefix)
        {
            Calls.Add("exports");
            Exports = (inputPath, outputPath, prefix);
        }

        public void Optimize(string inputPath, string outputPath, ComponentTarget target)
        {
            Calls.Add("optimize");
            Optimization = (inputPath, outputPath, target);
        }

        public void Delete(string path) => Calls.Add("delete:" + path);
    }
}
