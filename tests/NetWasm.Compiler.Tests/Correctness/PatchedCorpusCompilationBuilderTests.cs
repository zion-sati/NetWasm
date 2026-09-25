namespace NetWasm.Compiler.Tests.Correctness;

public sealed class PatchedCorpusCompilationBuilderTests
{
    [Fact]
    public void ShardOwnsItsPatchedAssemblyAndSourceInsteadOfCachedTemplateEvidence()
    {
        var builder = Assert.IsAssignableFrom<IPatchedCorpusCompilationBuilder>(new PatchedCorpusCompilationBuilder());
        var templateArtifact = new CorpusArtifact("template.dll", "template.pdb", "template-sha", "pdb-sha", "sdk", []);
        var template = new CorpusCompilation(CorrectnessTestAssets.CreateFixture("Template"), CilProfile.Release,
            templateArtifact, templateArtifact, "template-directory")
        {
            Sources = [new("Template.cs", "template-directory/Template.cs", "template-source-sha")],
        };
        var fixture = CorrectnessTestAssets.CreateFixture("Shard");
        var artifact = templateArtifact with { AssemblyPath = "shard-directory/patched.dll", AssemblySha256 = "patched-sha" };
        var source = new CorpusSourceArtifact("Shard.cs", "shard-directory/Shard.cs", "shard-source-sha");

        var compilation = builder.Build(template, fixture, artifact, source, "shard-directory");

        Assert.Same(fixture, compilation.Fixture);
        Assert.Same(artifact, compilation.Desktop);
        Assert.Same(compilation.Desktop, compilation.NetWasm);
        Assert.Same(source, Assert.Single(compilation.Sources));
        Assert.Equal("shard-directory", compilation.Directory);
        Assert.Equal(template.Profile, compilation.Profile);
        Assert.Equal("template-directory", template.Directory);
        Assert.Equal("template-source-sha", Assert.Single(template.Sources).Sha256);
        Assert.Same(templateArtifact, template.Desktop);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void MissingInputCannotProduceAnApparentlyValidCompilation(int missing)
    {
        var builder = Assert.IsAssignableFrom<IPatchedCorpusCompilationBuilder>(new PatchedCorpusCompilationBuilder());
        var fixture = CorrectnessTestAssets.CreateFixture();
        var artifact = new CorpusArtifact("assembly", "pdb", "sha", "pdb-sha", "sdk", []);
        var template = new CorpusCompilation(fixture, CilProfile.Debug, artifact, artifact, "template");
        var source = new CorpusSourceArtifact("Source.cs", "directory/Source.cs", "source-sha");

        Assert.ThrowsAny<ArgumentException>(() => builder.Build(missing == 0 ? null! : template,
            missing == 1 ? null! : fixture, missing == 2 ? null! : artifact, missing == 3 ? null! : source,
            missing == 4 ? " " : "directory"));
    }
}
