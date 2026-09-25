using System.Collections.Immutable;
using Microsoft.Extensions.DependencyInjection;

namespace NetWasm.Compiler.Tests.Correctness;

public sealed class RandomCilCampaignOwnershipTests
{
    [Fact]
    public async Task PreparationFailureRetainsEachSliceRootWithoutInvalidatingSharedTemplates()
    {
        var compiler = new Templates();
        var directories = new SliceDirectories(new RandomCilRunDirectoryFactory(new CorpusRunDirectoryFactory()));
        var sources = new FailingSources();
        await using var services = new ServiceCollection().AddCompilerCorrectnessHarness()
            .AddSingleton<IRoslynCorpusCompiler>(compiler)
            .AddSingleton<IRandomCilMetadataTokenResolver, Tokens>()
            .AddSingleton<IRandomCilInteractionShardPlanner, Planner>()
            .AddSingleton<IRandomCilCampaignScheduler, Scheduler>()
            .AddSingleton<IRandomCilRunDirectoryFactory>(directories)
            .AddSingleton<ICorpusSourceArtifactWriter>(sources)
            .BuildServiceProvider();
        var campaign = services.GetRequiredService<IRandomCilInteractionCampaign>();
        try
        {
            var first = Assert.Single(await campaign.RunAsync(1));
            var second = Assert.Single(await campaign.RunAsync(1));

            Assert.Equal(2, compiler.Compilations.Count);
            Assert.Equal([CilProfile.Debug, CilProfile.Release], compiler.Compilations.Select(item => item.Profile));
            Assert.Equal(2, directories.Created.Count);
            Assert.NotEqual(directories.Created[0].Root, directories.Created[1].Root);
            Assert.Equal(2, sources.Failures.Count);
            Assert.Same(sources.Failures[0], first.Failure);
            Assert.Same(sources.Failures[1], second.Failure);
            for (var index = 0; index < sources.Failures.Count; index++)
            {
                var root = directories.Created[index];
                Assert.Equal(root.Root, sources.Failures[index].Data["CorpusRunDirectory"]);
                Assert.Equal(compiler.Compilations[0].Directory, sources.Failures[index].Data["CorpusTemplateDirectory"]);
                Assert.True(Directory.Exists(root.Directory));
                Assert.Empty(Directory.EnumerateFileSystemEntries(root.Directory));
            }
        }
        finally
        {
            foreach (var directory in directories.Created) Directory.Delete(directory.Root, recursive: true);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TemplateFailureRetainsItsPathAndPreventsSliceAllocation(bool release)
    {
        var cause = new IOException("injected template failure");
        var compiler = new Templates { Failure = cause, FailureProfile = release ? CilProfile.Release : CilProfile.Debug };
        var directories = new SliceDirectories(new RandomCilRunDirectoryFactory(new CorpusRunDirectoryFactory()));
        await using var services = new ServiceCollection().AddCompilerCorrectnessHarness()
            .AddSingleton<IRoslynCorpusCompiler>(compiler)
            .AddSingleton<IRandomCilMetadataTokenResolver, Tokens>()
            .AddSingleton<IRandomCilInteractionShardPlanner, Planner>()
            .AddSingleton<IRandomCilRunDirectoryFactory>(directories)
            .BuildServiceProvider();
        var campaign = services.GetRequiredService<IRandomCilInteractionCampaign>();

        Assert.Same(cause, await Assert.ThrowsAsync<IOException>(() => campaign.RunAsync(1)));
        Assert.Same(cause, await Assert.ThrowsAsync<IOException>(() => campaign.RunAsync(1)));

        Assert.Equal(release ? 2 : 1, compiler.Compilations.Count);
        Assert.Equal(compiler.Compilations[^1].Directory, cause.Data["CorpusTemplateDirectory"]);
        Assert.Empty(directories.Created);
        Assert.False(cause.Data.Contains("CorpusRunDirectory"));
    }

    private sealed class Templates : IRoslynCorpusCompiler
    {
        public Exception? Failure { get; init; }
        public CilProfile? FailureProfile { get; init; }
        public List<CorpusCompilation> Compilations { get; } = [];
        public CorpusCompilation Compile(CorpusFixture fixture, CilProfile profile, string outputDirectory)
        {
            var artifact = new CorpusArtifact("unused.dll", "unused.pdb", "hash", "hash", "test", []);
            var result = new CorpusCompilation(fixture, profile, artifact, artifact, outputDirectory);
            Compilations.Add(result);
            if (FailureProfile == profile) throw Failure!;
            return result;
        }
    }

    private sealed class Tokens : IRandomCilMetadataTokenResolver
    {
        public RandomCilMetadataTokens Resolve(string assemblyPath) =>
            new(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);
    }

    private sealed class Planner : IRandomCilInteractionShardPlanner
    {
        public int CasesPerShard => 1;
        public ImmutableArray<RandomCilInteractionShard> CreateShards() =>
            [new(0, [new("owned-case", 1, ImmutableDictionary<string, string>.Empty)])];
    }

    private sealed class Scheduler : IRandomCilCampaignScheduler
    {
        public Task<ImmutableArray<RandomCilCampaignResult>> RunAsync(
            ImmutableArray<RandomCilCampaignCase> cases, CancellationToken cancellationToken = default) =>
            Task.FromResult(cases.Select(testCase => new RandomCilCampaignResult(testCase.Id, TimeSpan.Zero,
                Record.Exception(() => testCase.Execute(cancellationToken)))).ToImmutableArray());
    }

    private sealed class SliceDirectories(IRandomCilRunDirectoryFactory factory) : IRandomCilRunDirectoryFactory
    {
        public List<RandomCilRunDirectory> Created { get; } = [];
        public RandomCilRunDirectory Create(CilProfile profile, int shardIndex, int sliceIndex, bool captureDiagnostics)
        {
            var directory = factory.Create(profile, shardIndex, sliceIndex, captureDiagnostics);
            Created.Add(directory);
            return directory;
        }
    }

    private sealed class FailingSources : ICorpusSourceArtifactWriter
    {
        public List<IOException> Failures { get; } = [];
        public CorpusSourceArtifact Write(CorpusSourceFile source, string directory)
        {
            var failure = new IOException("injected source preparation failure");
            Failures.Add(failure);
            throw failure;
        }
    }
}
