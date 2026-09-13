using NetWasm.Compiler.ComponentModel.Raw;

namespace NetWasm.Compiler.ComponentModel.Tests.Raw;

public sealed class RawModuleLinkerTests
{
    private static readonly string[] ExpectedStages = ["validate", "create", "execute"];

    [Theory]
    [InlineData("wasm32")]
    [InlineData("wasm64")]
    public void ValidatesThenAcquiresAndTransfersWorkspaceToExecution(string width)
    {
        var stages = new RecordingStages();
        var request = new RawModuleLinkRequest("application", "runtime", "output", new(width, "0.2", "utf8"));
        var linker = CreateLinker(stages);

        linker.Link(request);

        Assert.Equal(["validate", "create", "execute"], stages.Calls);
        Assert.Same(request, stages.Validated);
        Assert.Same(request, stages.Executed);
        Assert.Equal("output", stages.OutputPath);
        Assert.Same(stages.Workspace, stages.ExecutedWorkspace);
    }

    [Theory]
    [InlineData("validate", 1)]
    [InlineData("create", 2)]
    [InlineData("execute", 3)]
    public void StopsAtFirstFailure(string stage, int calls)
    {
        var stages = new RecordingStages { FailingStage = stage };
        var linker = CreateLinker(stages);

        Assert.Same(stages.Failure, Assert.Throws<InvalidOperationException>(() => linker.Link(
            new("application", "runtime", "output", ComponentTarget.Wasm32Wasi02))));
        Assert.Equal(ExpectedStages.Take(calls), stages.Calls);
    }

    [Fact]
    public void RejectsMissingDependenciesAndRequestBeforeSideEffects()
    {
        var stages = new RecordingStages();
        Assert.Throws<ArgumentNullException>(() => new RawModuleLinker(null!, stages, stages));
        Assert.Throws<ArgumentNullException>(() => new RawModuleLinker(stages, null!, stages));
        Assert.Throws<ArgumentNullException>(() => new RawModuleLinker(stages, stages, null!));
        var linker = CreateLinker(stages);
        Assert.Throws<ArgumentNullException>(() => linker.Link(null!));
        Assert.Empty(stages.Calls);
    }

    private static IRawModuleLinker CreateLinker(RecordingStages stages) =>
        Assert.IsAssignableFrom<IRawModuleLinker>(new RawModuleLinker(stages, stages, stages));

    private sealed class RecordingStages : IRawModuleLinkInputValidator,
        IComponentPackageWorkspaceFactory, IRawModuleLinkExecution, IDirectoryDeleter
    {
        public List<string> Calls { get; } = [];
        public string? FailingStage { get; init; }
        public InvalidOperationException Failure { get; } = new();
        public RawModuleLinkRequest? Validated { get; private set; }
        public RawModuleLinkRequest? Executed { get; private set; }
        public string? OutputPath { get; private set; }
        public ComponentPackageWorkspace? Workspace { get; private set; }
        public ComponentPackageWorkspace? ExecutedWorkspace { get; private set; }

        public void Validate(RawModuleLinkRequest request)
        {
            Enter("validate");
            Validated = request;
        }

        public ComponentPackageWorkspace Create(string outputPath)
        {
            Enter("create");
            OutputPath = outputPath;
            return Workspace = new(this, "temporary", "linked", "embedded", "component");
        }

        public void Run(RawModuleLinkRequest request, ComponentPackageWorkspace workspace)
        {
            Enter("execute");
            Executed = request;
            ExecutedWorkspace = workspace;
        }

        public void Delete(string path) => throw new InvalidOperationException("execution owns cleanup");

        private void Enter(string stage)
        {
            Calls.Add(stage);
            if (stage == FailingStage) throw Failure;
        }
    }
}
