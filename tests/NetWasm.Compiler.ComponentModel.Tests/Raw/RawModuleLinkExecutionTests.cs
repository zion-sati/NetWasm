using NetWasm.Compiler.ComponentModel.Raw;

namespace NetWasm.Compiler.ComponentModel.Tests.Raw;

public sealed class RawModuleLinkExecutionTests
{
    private static readonly string[] ExpectedStages = ["environment", "merge", "optimize", "publish"];

    [Theory]
    [InlineData("wasm32")]
    [InlineData("wasm64")]
    public void PreservesRawExportsAndPublishesOnlyAfterOptimization(string width)
    {
        var stages = new RecordingStages();
        var request = new RawModuleLinkRequest("application", "runtime", "output", new(width, "0.2", "utf8"));

        CreateExecution(stages).Run(request, CreateWorkspace(stages));

        Assert.Equal(["environment", "merge", "optimize", "publish", "delete"], stages.Calls);
        Assert.Equal((Path.Combine("temporary", "environment.wasm"), request.Target), stages.Environment);
        Assert.Equal(new ComponentCoreModuleMergeRequest("application", "runtime",
            Path.Combine("temporary", "environment.wasm"), Path.Combine("temporary", "merged.wasm"), request.Target), stages.Merge);
        Assert.Equal((Path.Combine("temporary", "merged.wasm"), "linked", request.Target), stages.Optimization);
        Assert.Equal(("linked", "output"), stages.Publication);
        Assert.Equal("temporary", stages.DeletedDirectory);
    }

    [Theory]
    [InlineData("environment", 1)]
    [InlineData("merge", 2)]
    [InlineData("optimize", 3)]
    [InlineData("publish", 4)]
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
        Assert.Throws<ArgumentNullException>(() => new RawModuleLinkExecution(null!, stages, stages, stages));
        Assert.Throws<ArgumentNullException>(() => new RawModuleLinkExecution(stages, null!, stages, stages));
        Assert.Throws<ArgumentNullException>(() => new RawModuleLinkExecution(stages, stages, null!, stages));
        Assert.Throws<ArgumentNullException>(() => new RawModuleLinkExecution(stages, stages, stages, null!));
        Assert.Empty(stages.Calls);
    }

    private static IRawModuleLinkExecution CreateExecution(RecordingStages stages) =>
        Assert.IsAssignableFrom<IRawModuleLinkExecution>(new RawModuleLinkExecution(stages, stages, stages, stages));

    private static ComponentPackageWorkspace CreateWorkspace(RecordingStages stages) =>
        new(stages, "temporary", "linked", "embedded", "component");

    private sealed class RecordingStages : IEmscriptenEnvironmentShimWriter,
        IComponentCoreModuleMergeRunner, IComponentCoreModuleOptimizer, IFileMover, IDirectoryDeleter
    {
        public List<string> Calls { get; } = [];
        public string? FailingStage { get; init; }
        public InvalidOperationException Failure { get; } = new();
        public (string, ComponentTarget)? Environment { get; private set; }
        public ComponentCoreModuleMergeRequest? Merge { get; private set; }
        public (string, string, ComponentTarget)? Optimization { get; private set; }
        public (string, string)? Publication { get; private set; }
        public string? DeletedDirectory { get; private set; }

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

        public void Optimize(string inputPath, string outputPath, ComponentTarget target)
        {
            Enter("optimize");
            Optimization = (inputPath, outputPath, target);
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
